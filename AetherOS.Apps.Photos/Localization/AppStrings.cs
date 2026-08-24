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
        // added after update 2.0.1.0 (photos settings)
        ["os.photos_settings"] = "Photos settings",
        ["os.photos_menu"] = "Menu",
        ["os.photos_open_folder"] = "Open folder on disk",
        ["os.photos_settings_import"] = "Automatic imports",
        ["os.photos_auto_camera"] = "Photos you take with the Camera app",
        ["os.photos_auto_camera_hint"] = "Everything you shoot with the camera shutter is kept in your camera roll.",
        ["os.photos_auto_captures"] = "Photos you take for another app",
        ["os.photos_auto_captures_hint"] = "When another app asks for a photo (a profile picture, a chat image, your phone avatar), keep a copy in the camera roll as well. The photo still reaches the app that asked for it either way.",
        ["os.photos_auto_printscreens"] = "Screenshots you take in the game",
        ["os.photos_auto_printscreens_hint"] = "New game screenshots are added to the Printscreens album.",
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
        // added after update 2.0.1.0 (photos settings)
        ["os.photos_settings"] = "Foto-Einstellungen",
        ["os.photos_menu"] = "Menü",
        ["os.photos_open_folder"] = "Ordner auf der Festplatte öffnen",
        ["os.photos_settings_import"] = "Automatischer Import",
        ["os.photos_auto_camera"] = "Fotos, die du mit der Kamera-App machst",
        ["os.photos_auto_camera_hint"] = "Alles, was du mit dem Auslöser aufnimmst, bleibt in deinem Kamera-Album.",
        ["os.photos_auto_captures"] = "Fotos, die du für eine andere App machst",
        ["os.photos_auto_captures_hint"] = "Wenn eine andere App ein Foto anfragt (Profilbild, Chatbild, Avatar deines Handys), wird zusätzlich eine Kopie im Kamera-Album abgelegt. Das Foto geht so oder so an die App, die danach gefragt hat.",
        ["os.photos_auto_printscreens"] = "Screenshots, die du im Spiel machst",
        ["os.photos_auto_printscreens_hint"] = "Neue Spiel-Screenshots landen im Album Screenshots.",
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
        // added after update 2.0.1.0 (photos settings)
        ["os.photos_settings"] = "Ajustes de Fotos",
        ["os.photos_menu"] = "Menú",
        ["os.photos_open_folder"] = "Abrir la carpeta en el disco",
        ["os.photos_settings_import"] = "Importación automática",
        ["os.photos_auto_camera"] = "Fotos que haces con la app Cámara",
        ["os.photos_auto_camera_hint"] = "Todo lo que dispares con la cámara se guarda en tu carrete.",
        ["os.photos_auto_captures"] = "Fotos que haces para otra app",
        ["os.photos_auto_captures_hint"] = "Cuando otra app te pide una foto (una imagen de perfil, una imagen para el chat, el avatar del móvil), también se guarda una copia en el carrete. La foto llega igual a la app que la pidió.",
        ["os.photos_auto_printscreens"] = "Capturas que haces en el juego",
        ["os.photos_auto_printscreens_hint"] = "Las capturas nuevas del juego se añaden al álbum Capturas.",
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
        // added after update 2.0.1.0 (photos settings)
        ["os.photos_settings"] = "Réglages de Photos",
        ["os.photos_menu"] = "Menu",
        ["os.photos_open_folder"] = "Ouvrir le dossier sur le disque",
        ["os.photos_settings_import"] = "Imports automatiques",
        ["os.photos_auto_camera"] = "Photos prises avec l'app Appareil photo",
        ["os.photos_auto_camera_hint"] = "Tout ce que vous prenez avec le déclencheur reste dans votre pellicule.",
        ["os.photos_auto_captures"] = "Photos prises pour une autre app",
        ["os.photos_auto_captures_hint"] = "Quand une autre app demande une photo (photo de profil, image de discussion, avatar du téléphone), une copie est aussi conservée dans la pellicule. La photo part de toute façon vers l'app qui l'a demandée.",
        ["os.photos_auto_printscreens"] = "Captures prises dans le jeu",
        ["os.photos_auto_printscreens_hint"] = "Les nouvelles captures du jeu sont ajoutées à l'album Captures.",
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
        // added after update 2.0.1.0 (photos settings)
        ["os.photos_settings"] = "Configurações de Fotos",
        ["os.photos_menu"] = "Menu",
        ["os.photos_open_folder"] = "Abrir a pasta no disco",
        ["os.photos_settings_import"] = "Importação automática",
        ["os.photos_auto_camera"] = "Fotos que você tira no app Câmera",
        ["os.photos_auto_camera_hint"] = "Tudo o que você fotografar com o obturador fica no seu rolo da câmera.",
        ["os.photos_auto_captures"] = "Fotos que você tira para outro app",
        ["os.photos_auto_captures_hint"] = "Quando outro app pede uma foto (foto de perfil, imagem de conversa, avatar do celular), uma cópia também fica no rolo da câmera. A foto chega ao app que pediu de qualquer jeito.",
        ["os.photos_auto_printscreens"] = "Capturas que você tira no jogo",
        ["os.photos_auto_printscreens_hint"] = "As novas capturas do jogo entram no álbum Capturas.",
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
        // added after update 2.0.1.0 (photos settings)
        ["os.photos_settings"] = "Настройки Фото",
        ["os.photos_menu"] = "Меню",
        ["os.photos_open_folder"] = "Открыть папку на диске",
        ["os.photos_settings_import"] = "Автоматический импорт",
        ["os.photos_auto_camera"] = "Снимки, сделанные в приложении Камера",
        ["os.photos_auto_camera_hint"] = "Всё, что вы снимаете затвором камеры, остаётся в галерее снимков.",
        ["os.photos_auto_captures"] = "Снимки, сделанные для другого приложения",
        ["os.photos_auto_captures_hint"] = "Когда другое приложение просит фото (аватар профиля, картинку для чата, аватар телефона), копия дополнительно останется в галерее снимков. Само фото в любом случае уйдёт туда, где его запросили.",
        ["os.photos_auto_printscreens"] = "Скриншоты, сделанные в игре",
        ["os.photos_auto_printscreens_hint"] = "Новые скриншоты из игры попадают в альбом Скриншоты.",
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
