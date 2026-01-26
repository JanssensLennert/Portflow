'use server'

import {redirect} from 'next/navigation'
import {protectedFormAction} from '@/lib/serverFunctions'
import {createLpSchema} from '@/schemas/lpSchemas'
import {createLp, updateLp as updateLpDb, getLpById, deleteLp} from '@/dal/lp'
import {findOrCreateArtistByName} from '@/dal/artist'
import {findOrCreateLabelByName} from '@/dal/label'
import {findOrCreateGenreByNameForUser} from '@/dal/genre'
import {getSessionProfileFromCookieOrThrow} from '@/lib/sessionUtils'
import {getLogger} from '@/lib/logger'
import {saveUploadedFile} from '@/lib/fileUpload'

export const createLpAction = protectedFormAction({
  schema: createLpSchema,
  functionName: 'Create LP action',
  globalErrorMessage: 'Kon LP niet opslaan. Probeer opnieuw.',
  serverFn: async ({data, logger, profile}, _prevState, formData) => {
    const start = Date.now()
    
    if (!profile) {
      logger.error('No profile found - user not authenticated')
      throw new Error('Unauthorized')
    }

    logger.info(`LP create request by user ${profile.id}`)

    const artist = await findOrCreateArtistByName(data.artist)
    const labelName = (data.label ?? '').trim()
    const label = labelName ? await findOrCreateLabelByName(labelName) : await findOrCreateLabelByName('Onbekend')
    
    // Find or create genre
    const genre = data.genre ? await findOrCreateGenreByNameForUser(data.genre, profile.id) : null

    // Handle cover image - check for file upload first, then URL
    let coverUrl = data.cover ?? ''
    if (formData instanceof FormData) {
      const coverFile = formData.get('coverFile') as File
      if (coverFile && coverFile.size > 0) {
        try {
          coverUrl = await saveUploadedFile(coverFile)
          logger.info(`Cover image uploaded: ${coverUrl}`)
        } catch (error) {
          logger.error(`Failed to upload cover: ${error}`)
        }
      }
    }

    // Parse tracks from form data and convert duration to seconds
    const tracks = data.tracks?.filter(t => t?.title && t?.duration).map(track => {
      const [minutes, seconds] = track.duration.split(':').map(Number)
      const durationInSeconds = minutes * 60 + seconds
      
      return {
        title: track.title,
        duration: durationInSeconds,
        artistId: artist.id,
      }
    }) || []

    const lp = await createLp({
      title: data.title,
      year: parseInt(data.year),
      condition: data.condition,
      coverUrl: coverUrl,
      notes: data.notes ?? null,
      artistId: artist.id,
      labelId: label.id,
      userId: profile.id,
      genreIds: genre ? [genre.id] : undefined,
      tracks,
    })

    // tijdelijk geen wishlist toevoegen
    // await addLpToUserWishlist(profile.id, lp.id)

    const duration = Date.now() - start
    logger.info(`LP '${lp.title}' (ID: ${lp.id}) created successfully in ${duration}ms`)

    redirect('/')
  },
})

export async function deleteLpAction(lpId: string) {
  'use server'
  
  const profile = await getSessionProfileFromCookieOrThrow()
  const logger = await getLogger()
  const start = Date.now()

  logger.info(`Delete LP action called for LP ${lpId} by user ${profile.id}`)

  // Controleer of de gebruiker eigenaar is
  const lp = await getLpById(lpId)
  if (!lp) {
    throw new Error('LP niet gevonden')
  }

  if (lp.userId !== profile.id) {
    logger.warn(`User ${profile.id} tried to delete LP ${lpId} they don't own`)
    throw new Error('Je kan alleen je eigen LPs verwijderen')
  }

  // Verwijder de LP
  await deleteLp(lpId)

  const duration = Date.now() - start
  logger.info(`LP ${lpId} deleted by user ${profile.id} in ${duration}ms`)

  redirect('/')
}

// Bound action voor edit form - lpId is already bound
export async function createUpdateLpAction(lpId: string) {
  'use server'
  
  return async (formData: FormData) => {
    'use server'
    // Add lpId to formData
    formData.set('lpId', lpId)
    return updateLpServerAction({success: false}, formData)
  }
}

export const updateLpServerAction = protectedFormAction({
  schema: createLpSchema,
  functionName: 'Update LP action',
  globalErrorMessage: 'Kon LP niet bijwerken. Probeer opnieuw.',
  serverFn: async ({data, logger, profile}, _prevState, formData) => {
    const start = Date.now()
    
    if (!profile) throw new Error('Unauthorized')

    const lpId = formData instanceof FormData ? formData.get('lpId') as string : null
    
    if (!lpId) {
      throw new Error('LP ID ontbreekt')
    }

    logger.info(`LP update request for LP ${lpId} by user ${profile.id}`)

    // Controleer eigenaarschap
    const lp = await getLpById(lpId)
    if (!lp || lp.userId !== profile.id) {
      logger.warn(`User ${profile.id} tried to update LP ${lpId} they don't own`)
      throw new Error('Je kan alleen je eigen LPs bewerken')
    }

    const artist = await findOrCreateArtistByName(data.artist)
    const labelName = (data.label ?? '').trim()
    const label = labelName ? await findOrCreateLabelByName(labelName) : await findOrCreateLabelByName('Onbekend')
    
    // Find or create genre
    const genre = data.genre ? await findOrCreateGenreByNameForUser(data.genre, profile.id) : null

    await updateLpDb(lpId, {
      title: data.title,
      year: parseInt(data.year),
      condition: data.condition,
      coverUrl: data.cover ?? '',
      notes: data.notes ?? null,
      artist: {connect: {id: artist.id}},
      label: {connect: {id: label.id}},
      genres: genre ? {
        set: [], // eerst alle genres verwijderen
        connect: [{id: genre.id}] // dan de nieuwe genre toevoegen
      } : undefined,
    })

    const duration = Date.now() - start
    logger.info(`LP ${lpId} updated by user ${profile.id} in ${duration}ms`)

    redirect(`/lp/${lpId}`)
  },
})
