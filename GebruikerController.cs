using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Restaurant.Models;
using Restaurant.ViewModels;
using Restaurant.Services;
using Restaurant.Data.UnitOfWork;

namespace Restaurant.Controllers
{
    public class GebruikerController : Controller
    {
        private readonly UserManager<CustomUser> _userManager;
        private readonly SignInManager<CustomUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IMapper _mapper;
        private readonly IUnitOfWork _uow;
        private readonly IMailService _mailService;

        public GebruikerController(
            UserManager<CustomUser> userManager,
            SignInManager<CustomUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            IMapper mapper,
            IUnitOfWork uow,
            IMailService mailService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _mapper = mapper;
            _uow = uow;
            _mailService = mailService;
        }

        // =========================
        // LOGGING HELPER
        // =========================
        private async Task LogAsync(string actie, string boodschap, string? userId = null)
        {
            var log = new Logboek
            {
                Datum = DateTime.Now,
                UserId = userId ?? User.Identity?.Name ?? "Onbekend",
                Actie = actie,
                Boodschap = boodschap
            };
            await _uow.LogboekRepository.AddAsync(log);
            await _uow.SaveAsync();
        }

        // =========================
        // LOGIN / LOGOUT
        // =========================

        [AllowAnonymous]
        public IActionResult Login() => View(new GebruikerLoginViewModel());

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(GebruikerLoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByNameAsync(model.Gebruikersnaam);
            if (user == null)
            {
                ModelState.AddModelError("", "Verkeerde gebruikersnaam of wachtwoord.");
                await LogAsync("Login mislukt", $"Gebruiker '{model.Gebruikersnaam}' niet gevonden");
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(user.UserName, model.Password, false, false);

            if (result.IsLockedOut)
            {
                ModelState.AddModelError("", "Account geblokkeerd.");
                await LogAsync("Login geblokkeerd", $"Gebruiker '{user.UserName}' geblokkeerd", user.Id);
                return View(model);
            }

            if (!result.Succeeded)
            {
                ModelState.AddModelError("", "Verkeerde gebruikersnaam of wachtwoord.");
                await LogAsync("Login mislukt", $"Onjuist wachtwoord voor '{user.UserName}'", user.Id);
                return View(model);
            }

            await LogAsync("Login succesvol", $"Gebruiker '{user.UserName}' ingelogd", user.Id);
            return RedirectToAction("Index", "Home");
        }

        [AllowAnonymous]
        public async Task<IActionResult> Logout()
        {
            var user = await _userManager.GetUserAsync(User);
            await _signInManager.SignOutAsync();
            if (user != null)
                await LogAsync("Logout", $"Gebruiker '{user.UserName}' uitgelogd", user.Id);

            return RedirectToAction("Index", "Home");
        }

        // =========================
        // FORGOT PASSWORD
        // =========================
        [AllowAnonymous]
        public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                await LogAsync("Wachtwoord vergeten", $"E-mail '{model.Email}' niet gevonden");
                return RedirectToAction(nameof(ForgotPasswordConfirmation));
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetLink = Url.Action(nameof(ResetPassword), "Gebruiker", new { token, email = model.Email }, Request.Scheme);

            var body = $@"
                <h2>Wachtwoord resetten</h2>
                <p>Klik op de link om uw wachtwoord te resetten:</p>
                <p><a href='{resetLink}'>Wachtwoord resetten</a></p>";

            await _mailService.SendEmailAsync(model.Email, "Wachtwoord resetten", body);

            await LogAsync("Wachtwoord vergeten", $"Resetlink verstuurd naar '{model.Email}'", user.Id);
            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        [AllowAnonymous]
        public IActionResult ForgotPasswordConfirmation() => View();

        // =========================
        // RESET PASSWORD
        // =========================
        [AllowAnonymous]
        public IActionResult ResetPassword(string token, string email)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(email))
                return RedirectToAction(nameof(Login));

            return View(new ResetPasswordViewModel { Token = token, Email = email });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null) return RedirectToAction(nameof(ResetPasswordConfirmation));

            var result = await _userManager.ResetPasswordAsync(user, model.Token, model.Password);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
                await LogAsync("Reset wachtwoord mislukt", $"Gebruiker '{user.UserName}' fout bij reset wachtwoord", user.Id);
                return View(model);
            }

            await LogAsync("Reset wachtwoord succesvol", $"Gebruiker '{user.UserName}' heeft wachtwoord gereset", user.Id);
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        [AllowAnonymous]
        public IActionResult ResetPasswordConfirmation() => View();

        // =========================
        // OVERZICHT GEBRUIKERS
        // =========================
        [Authorize]
        public async Task<IActionResult> Index()
        {
            var gebruikers = await _userManager.Users.ToListAsync();
            var rollen = _roleManager.Roles.Select(r => r.Name).ToList();
            var dict = rollen.ToDictionary(r => r, r => new List<CustomUser>());

            foreach (var gebruiker in gebruikers)
            {
                var userRoles = await _userManager.GetRolesAsync(gebruiker);
                foreach (var rol in userRoles)
                {
                    if (!dict.ContainsKey(rol)) dict[rol] = new List<CustomUser>();
                    dict[rol].Add(gebruiker);
                }
            }

            return View(new GebruikerListViewModel { GebruikersPerRol = dict });
        }

        // =========================
        // EDIT GEBRUIKER
        // =========================
        [Authorize]
        public async Task<IActionResult> Edit(string id)
        {
            var gebruiker = await _userManager.FindByIdAsync(id);
            if (gebruiker == null) return RedirectToAction(nameof(Index));

            var model = _mapper.Map<GebruikerEditViewModel>(gebruiker);
            model.Rollen = new SelectList(_roleManager.Roles, "Name", "Name");
            model.RolNaam = (await _userManager.GetRolesAsync(gebruiker)).FirstOrDefault();
            return View(model);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(GebruikerEditViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.Rollen = new SelectList(_roleManager.Roles, "Name", "Name");
                return View(model);
            }

            var gebruiker = await _userManager.FindByIdAsync(model.Id);
            if (gebruiker == null) return RedirectToAction(nameof(Index));

            _mapper.Map(model, gebruiker);

            if (!string.IsNullOrWhiteSpace(model.Password))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(gebruiker);
                var pwdResult = await _userManager.ResetPasswordAsync(gebruiker, token, model.Password);
                if (!pwdResult.Succeeded)
                {
                    AddErrors(pwdResult);
                    model.Rollen = new SelectList(_roleManager.Roles, "Name", "Name");
                    return View(model);
                }
            }

            var updateResult = await _userManager.UpdateAsync(gebruiker);
            if (!updateResult.Succeeded)
            {
                AddErrors(updateResult);
                model.Rollen = new SelectList(_roleManager.Roles, "Name", "Name");
                return View(model);
            }

            var currentRoles = await _userManager.GetRolesAsync(gebruiker);
            await _userManager.RemoveFromRolesAsync(gebruiker, currentRoles);
            if (!string.IsNullOrEmpty(model.RolNaam))
                await _userManager.AddToRoleAsync(gebruiker, model.RolNaam);

            await LogAsync("Gebruiker bewerkt", $"Gebruiker '{gebruiker.UserName}' aangepast", gebruiker.Id);

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // CREATE GEBRUIKER
        // =========================
        [AllowAnonymous]
        public IActionResult Create() => View(new GebruikerCreateViewModel
        {
            Geboortedatum = new DateTime(1950, 1, 1),
            Rollen = new SelectList(_roleManager.Roles, "Name", "Name")
        });

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(GebruikerCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.Rollen = new SelectList(_roleManager.Roles, "Name", "Name");
                return View(model);
            }

            var landen = await _uow.LandRepository.GetAllAsync();
            var land = landen.FirstOrDefault() ?? new Land { Naam = "België" };
            if (land.Id == 0)
            {
                await _uow.LandRepository.AddAsync(land);
                await _uow.SaveAsync();
            }

            var gebruiker = _mapper.Map<CustomUser>(model);
            gebruiker.EmailConfirmed = true;
            gebruiker.LandId = land.Id;

            var result = await _userManager.CreateAsync(gebruiker, model.Password);
            if (!result.Succeeded)
            {
                AddErrors(result);
                model.Rollen = new SelectList(_roleManager.Roles, "Name", "Name");
                return View(model);
            }

            if (_userManager.Users.Count() == 1)
                await _userManager.AddToRoleAsync(gebruiker, "Eigenaar");

            await _userManager.AddToRoleAsync(gebruiker, string.IsNullOrEmpty(model.RolNaam) ? "Gebruiker" : model.RolNaam);

            await LogAsync("Gebruiker aangemaakt", $"Nieuwe gebruiker '{gebruiker.UserName}' met rol '{model.RolNaam}' aangemaakt", gebruiker.Id);

            return RedirectToAction(nameof(Login));
        }

        // =========================
        // DELETE GEBRUIKER
        // =========================
        [Authorize]
        public async Task<IActionResult> Delete(string id)
        {
            var gebruiker = await _userManager.FindByIdAsync(id);
            if (gebruiker == null) return NotFound();
            return View(_mapper.Map<GebruikerDeleteViewModel>(gebruiker));
        }

        [HttpPost, ActionName("Delete")]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            var gebruiker = await _userManager.FindByIdAsync(id);
            if (gebruiker == null) return RedirectToAction(nameof(Index));

            var result = await _userManager.DeleteAsync(gebruiker);
            if (!result.Succeeded)
            {
                AddErrors(result);
                return View("Delete", _mapper.Map<GebruikerDeleteViewModel>(gebruiker));
            }

            await LogAsync("Gebruiker verwijderd", $"Gebruiker '{gebruiker.UserName}' is verwijderd", gebruiker.Id);
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // ACCOUNT
        // =========================
        [Authorize]
        public async Task<IActionResult> Account()
        {
            var gebruiker = await _userManager.GetUserAsync(User);
            if (gebruiker == null)
            {
                await _signInManager.SignOutAsync();
                return RedirectToAction("Login", "Gebruiker");
            }

            return View(new GebruikerAccountViewModel
            {
                GebruikersNaam = gebruiker.UserName,
                Email = gebruiker.Email,
                Voornaam = gebruiker.Voornaam,
                Naam = gebruiker.Achternaam,
                Adres = gebruiker.Adres,
                Huisnummer = gebruiker.Huisnummer,
                Postcode = gebruiker.Postcode,
                Gemeente = gebruiker.Gemeente
            });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Account(GebruikerAccountViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var gebruiker = await _userManager.GetUserAsync(User);
            if (gebruiker == null) return RedirectToAction("Login");

            gebruiker.Voornaam = model.Voornaam ?? gebruiker.Voornaam;
            gebruiker.Achternaam = model.Naam ?? gebruiker.Achternaam;
            gebruiker.Adres = model.Adres ?? gebruiker.Adres;
            gebruiker.Huisnummer = model.Huisnummer ?? gebruiker.Huisnummer;
            gebruiker.Postcode = model.Postcode ?? gebruiker.Postcode;
            gebruiker.Gemeente = model.Gemeente ?? gebruiker.Gemeente;

            if (!string.IsNullOrWhiteSpace(model.Email) && model.Email != gebruiker.Email)
                await _userManager.SetEmailAsync(gebruiker, model.Email);

            var updateResult = await _userManager.UpdateAsync(gebruiker);
            if (!updateResult.Succeeded)
            {
                AddErrors(updateResult);
                return View(model);
            }

            if (!string.IsNullOrWhiteSpace(model.NieuwWachtwoord) ||
                !string.IsNullOrWhiteSpace(model.BevestigNieuwWachtwoord))
            {
                if (string.IsNullOrWhiteSpace(model.OudWachtwoord))
                {
                    ModelState.AddModelError("", "Geef je huidige wachtwoord in.");
                    return View(model);
                }

                var pwdResult = await _userManager.ChangePasswordAsync(
                    gebruiker, model.OudWachtwoord, model.NieuwWachtwoord);

                if (!pwdResult.Succeeded)
                {
                    // Soms falen ChangePasswordAsync bij speciale tekens/spaties; probeer fallback via reset token
                    var token = await _userManager.GeneratePasswordResetTokenAsync(gebruiker);
                    var resetResult = await _userManager.ResetPasswordAsync(gebruiker, token, model.NieuwWachtwoord);
                    if (!resetResult.Succeeded)
                    {
                        AddErrors(pwdResult);
                        AddErrors(resetResult);
                        return View(model);
                    }
                }
            }

            await LogAsync("Account bijgewerkt", $"Gebruiker '{gebruiker.UserName}' heeft account aangepast", gebruiker.Id);

            ViewBag.SuccessMessage = "Account succesvol bijgewerkt.";
            return View(model);
        }

        // =========================
        // DELETE MY ACCOUNT
        // =========================
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMyAccount()
        {
            var gebruiker = await _userManager.GetUserAsync(User);
            if (gebruiker == null) return RedirectToAction("Login");

            await _signInManager.SignOutAsync();
            var result = await _userManager.DeleteAsync(gebruiker);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError("", error.Description);
                return RedirectToAction("Account");
            }

            await LogAsync("Account verwijderd", $"Gebruiker '{gebruiker.UserName}' heeft eigen account verwijderd", gebruiker.Id);

            return RedirectToAction("Index", "Home");
        }

        // =========================
        // HELPERS
        // =========================
        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Description);
        }

        // =========================
        // DASHBOARD (ADMIN)
        // =========================
        [Authorize(Roles = "Eigenaar,Kok,Ober,ZaalVerantwoordelijke")]
        public IActionResult Dashboard() => View();
    }
}
