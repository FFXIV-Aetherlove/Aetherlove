namespace AetherLove.Services.Localization;

internal static class HubErrorsFr
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["huberror.generic"] = "Une erreur serveur inattendue s'est produite.",
        ["huberror.generic_detail"] = "Une erreur s'est produite : {0}",
        ["huberror.invalid_request"] = "Le serveur a rejeté la requête. Si cela se reproduit, mettez à jour le plugin.",
        ["huberror.unauthenticated"] = "Votre session n'est plus valide. Veuillez vous reconnecter.",
        ["huberror.banned"] = "Votre compte a été banni.",
        ["huberror.rate_limited"] = "Vous faites cela trop souvent. Veuillez réessayer dans un instant.",
        ["huberror.profile_not_found"] = "Profil introuvable.",
        ["huberror.profile_not_visible"] = "Ce profil n'est pas disponible.",
        ["huberror.deck_expired"] = "Ce profil n'est plus dans votre deck. Actualisez votre deck et réessayez.",
        ["huberror.no_active_match"] = "Vous n'êtes plus en match avec ce joueur.",
        ["huberror.peer_keys_missing"] = "Cet utilisateur n'a pas encore configuré le chiffrement E2E et ne peut pas discuter, réessayez plus tard.",
        ["huberror.key_bundle_exists"] = "Les clés de chiffrement sont déjà configurées pour ce compte.",
        ["huberror.message_too_large"] = "Ce message est trop long pour être envoyé.",
        ["huberror.bio_too_long"] = "Votre bio dépasse la limite de {0} caractères.",
        ["huberror.lalafell_erp"] = "Le roleplay adulte n'est pas disponible pour les personnages Lalafell.",
        ["huberror.lalafell_nsfw"] = "Les fonctionnalités NSFW ne sont pas disponibles pour les personnages Lalafell.",
        ["huberror.lalafell_nsfw_photo"] = "Les photos NSFW ne sont pas disponibles pour les personnages Lalafell.",
        ["huberror.nsfw_disable_blocked"] = "Retirez vos photos NSFW et désactivez le roleplay 18+ avant de désactiver le NSFW.",
        ["huberror.img_too_large"] = "L'image est trop volumineuse ({0} Mo). Le maximum est de {1} Mo.",
        ["huberror.img_dimensions_too_large"] = "L'image est trop grande ({0}×{1}). Le côté le plus long ne peut dépasser {2}px.",
        ["huberror.img_crop_too_small"] = "La zone de recadrage est trop petite (min {0}px par côté).",
        ["huberror.img_decode_failed"] = "Impossible de lire l'image. Formats pris en charge : PNG, JPEG, WebP, GIF.",
        ["huberror.img_payload_invalid"] = "La photo n'a pas pu être envoyée. Veuillez resélectionner l'image.",
        ["huberror.report_self"] = "Vous ne pouvez pas vous signaler vous-même.",
        ["huberror.report_reason_required"] = "Veuillez décrire le problème.",
        ["huberror.report_reason_too_long"] = "Cette description est trop longue (max {0} caractères).",
        ["huberror.report_target_gone"] = "Ce profil n'existe plus.",
        ["huberror.report_duplicate"] = "Vous avez déjà signalé cet utilisateur récemment. Notre équipe examine le cas.",
        ["huberror.feedback_required"] = "Veuillez saisir un message avant d'envoyer.",
    };
}
