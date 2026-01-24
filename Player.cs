using UnityEngine;

public class Player : MonoBehaviour
{
    public float playerSpeed;
    private Rigidbody2D rb;
    private Vector2 playerDirection;

    public bool hasShield = false;
    public bool isScoreBoosted = false;
    private float scoreBoostTimer;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        float directionY = Input.GetAxisRaw("Vertical");
        playerDirection = new Vector2(0, directionY).normalized;

        if (isScoreBoosted)
        {
            scoreBoostTimer -= Time.deltaTime;
            if (scoreBoostTimer <= 0) isScoreBoosted = false;
        }
    }

    private void FixedUpdate()
    {
        rb.linearVelocity = new Vector2(0, playerDirection.y * playerSpeed);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("ScorePowerup"))
        {
            isScoreBoosted = true;
            scoreBoostTimer = 10f;
            Destroy(collision.gameObject);
        }
        else if (collision.CompareTag("ShieldPowerup"))
        {
            hasShield = true;
            Destroy(collision.gameObject);
        }
    }
}