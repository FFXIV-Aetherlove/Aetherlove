using System.Collections.Generic;

namespace AetherOS.Apps.Photos;

internal static class AppStrings
{
    public static readonly Dictionary<string, string> En = new()
    {
        ["os.photos_title"] = "Photos",
        ["os.photos_new_album"] = "New album",
        ["os.photos_album_name"] = "Album name",
        ["os.photos_import"] = "Import image",
        ["os.photos_selfie"] = "Take selfie",
        ["os.photos_empty"] = "No photos yet. Import one or take a selfie.",
        ["os.photos_delete_confirm"] = "Delete this? This cannot be undone.",
        ["os.photos_pick_banner"] = "Pick a photo to send",
        ["os.photos_rename"] = "Rename",
        ["os.photos_selfie_name"] = "Selfie",
        ["os.photos_move_title"] = "Move to album",
        ["os.photos_move_none"] = "No other albums yet.",
        ["os.photos_save_copy"] = "Save copy",
        ["os.photos_today"] = "Today",
        ["os.photos_yesterday"] = "Yesterday",
    };

    public static readonly Dictionary<string, string> De = new()
    {
        ["os.photos_title"] = "Fotos",
        ["os.photos_new_album"] = "Neues Album",
        ["os.photos_album_name"] = "Albumname",
        ["os.photos_import"] = "Bild importieren",
        ["os.photos_selfie"] = "Selfie aufnehmen",
        ["os.photos_empty"] = "Noch keine Fotos. Importiere eins oder mach ein Selfie.",
        ["os.photos_delete_confirm"] = "Wirklich löschen? Das kann nicht rückgängig gemacht werden.",
        ["os.photos_pick_banner"] = "Wähle ein Foto zum Senden",
        ["os.photos_rename"] = "Umbenennen",
        ["os.photos_selfie_name"] = "Selfie",
        ["os.photos_move_title"] = "In Album verschieben",
        ["os.photos_move_none"] = "Noch keine anderen Alben.",
        ["os.photos_save_copy"] = "Kopie speichern",
        ["os.photos_today"] = "Heute",
        ["os.photos_yesterday"] = "Gestern",
    };

    public static readonly Dictionary<string, string> Es = new()
    {
        ["os.photos_title"] = "Fotos",
        ["os.photos_new_album"] = "Nuevo álbum",
        ["os.photos_album_name"] = "Nombre del álbum",
        ["os.photos_import"] = "Importar imagen",
        ["os.photos_selfie"] = "Hacer un selfi",
        ["os.photos_empty"] = "Aún no hay fotos. Importa una o hazte un selfi.",
        ["os.photos_delete_confirm"] = "¿Eliminar esto? No se puede deshacer.",
        ["os.photos_pick_banner"] = "Elige una foto para enviar",
        ["os.photos_rename"] = "Renombrar",
        ["os.photos_selfie_name"] = "Selfi",
        ["os.photos_move_title"] = "Mover al álbum",
        ["os.photos_move_none"] = "Aún no hay otros álbumes.",
        ["os.photos_save_copy"] = "Guardar copia",
        ["os.photos_today"] = "Hoy",
        ["os.photos_yesterday"] = "Ayer",
    };

    public static readonly Dictionary<string, string> Fr = new()
    {
        ["os.photos_title"] = "Photos",
        ["os.photos_new_album"] = "Nouvel album",
        ["os.photos_album_name"] = "Nom de l'album",
        ["os.photos_import"] = "Importer une image",
        ["os.photos_selfie"] = "Prendre un selfie",
        ["os.photos_empty"] = "Aucune photo pour l'instant. Importez-en une ou prenez un selfie.",
        ["os.photos_delete_confirm"] = "Supprimer ceci ? Cette action est irréversible.",
        ["os.photos_pick_banner"] = "Choisissez une photo à envoyer",
        ["os.photos_rename"] = "Renommer",
        ["os.photos_selfie_name"] = "Selfie",
        ["os.photos_move_title"] = "Déplacer vers un album",
        ["os.photos_move_none"] = "Pas encore d'autre album.",
        ["os.photos_save_copy"] = "Enregistrer une copie",
        ["os.photos_today"] = "Aujourd'hui",
        ["os.photos_yesterday"] = "Hier",
    };

    public static readonly Dictionary<string, string> Pt = new()
    {
        ["os.photos_title"] = "Fotos",
        ["os.photos_new_album"] = "Novo álbum",
        ["os.photos_album_name"] = "Nome do álbum",
        ["os.photos_import"] = "Importar imagem",
        ["os.photos_selfie"] = "Tirar selfie",
        ["os.photos_empty"] = "Ainda não há fotos. Importe uma ou tire uma selfie.",
        ["os.photos_delete_confirm"] = "Excluir isto? Isso não pode ser desfeito.",
        ["os.photos_pick_banner"] = "Escolha uma foto para enviar",
        ["os.photos_rename"] = "Renomear",
        ["os.photos_selfie_name"] = "Selfie",
        ["os.photos_move_title"] = "Mover para álbum",
        ["os.photos_move_none"] = "Ainda não há outros álbuns.",
        ["os.photos_save_copy"] = "Salvar cópia",
        ["os.photos_today"] = "Hoje",
        ["os.photos_yesterday"] = "Ontem",
    };

    public static readonly Dictionary<string, string> Ru = new()
    {
        ["os.photos_title"] = "Фото",
        ["os.photos_new_album"] = "Новый альбом",
        ["os.photos_album_name"] = "Название альбома",
        ["os.photos_import"] = "Импортировать изображение",
        ["os.photos_selfie"] = "Сделать селфи",
        ["os.photos_empty"] = "Пока нет фотографий. Импортируйте изображение или сделайте селфи.",
        ["os.photos_delete_confirm"] = "Удалить это? Действие нельзя отменить.",
        ["os.photos_pick_banner"] = "Выберите фото для отправки",
        ["os.photos_rename"] = "Переименовать",
        ["os.photos_selfie_name"] = "Селфи",
        ["os.photos_move_title"] = "Переместить в альбом",
        ["os.photos_move_none"] = "Других альбомов пока нет.",
        ["os.photos_save_copy"] = "Сохранить копию",
        ["os.photos_today"] = "Сегодня",
        ["os.photos_yesterday"] = "Вчера",
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Packs = new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["en"] = En,
        ["de"] = De,
        ["es"] = Es,
        ["fr"] = Fr,
        ["pt"] = Pt,
        ["ru"] = Ru,
    };
}
