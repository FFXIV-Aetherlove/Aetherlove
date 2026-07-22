using System.Collections.Generic;

namespace AetherOS.Apps.Camera;

internal static class AppStrings
{
    public static readonly Dictionary<string, string> En = new()
    {
        ["os.camera_hint"] = "The shot is framed in-game; the phone hides while you aim. Drag the frame to fit.",
        ["os.camera_roll"] = "Camera roll",
        ["os.camera_empty"] = "No photos yet. Take your first shot!",
        ["os.camera_for_app"] = "Photo requested by {0}",
        ["os.camera_retake"] = "Retake",
        ["os.camera_cancel"] = "Cancel",
        ["os.camera_delete_confirm"] = "Delete this photo? This cannot be undone.",
        ["os.camera_delete"] = "Delete",
        ["os.camera_review_title"] = "Happy with the shot?",
        ["os.camera_edit"] = "Edit",
        ["os.camera_save"] = "Save",
        ["os.camera_use"] = "Use photo",
    };

    public static readonly Dictionary<string, string> De = new()
    {
        ["os.camera_hint"] = "Das Foto wird im Spiel aufgenommen; das Telefon verschwindet, während du zielst. Zieh den Rahmen passend zurecht.",
        ["os.camera_roll"] = "Aufnahmen",
        ["os.camera_empty"] = "Noch keine Fotos. Mach deine erste Aufnahme!",
        ["os.camera_for_app"] = "Foto angefordert von {0}",
        ["os.camera_retake"] = "Erneut aufnehmen",
        ["os.camera_cancel"] = "Abbrechen",
        ["os.camera_delete_confirm"] = "Dieses Foto löschen? Das kann nicht rückgängig gemacht werden.",
        ["os.camera_delete"] = "Löschen",
        ["os.camera_review_title"] = "Zufrieden mit dem Foto?",
        ["os.camera_edit"] = "Bearbeiten",
        ["os.camera_save"] = "Speichern",
        ["os.camera_use"] = "Foto verwenden",
    };

    public static readonly Dictionary<string, string> Es = new()
    {
        ["os.camera_hint"] = "La foto se encuadra dentro del juego; el teléfono se oculta mientras apuntas. Arrastra el marco para ajustarlo.",
        ["os.camera_roll"] = "Carrete",
        ["os.camera_empty"] = "Aún no hay fotos. ¡Haz tu primera captura!",
        ["os.camera_for_app"] = "Foto solicitada por {0}",
        ["os.camera_retake"] = "Repetir",
        ["os.camera_cancel"] = "Cancelar",
        ["os.camera_delete_confirm"] = "¿Eliminar esta foto? No se puede deshacer.",
        ["os.camera_delete"] = "Eliminar",
        ["os.camera_review_title"] = "¿Te gusta la foto?",
        ["os.camera_edit"] = "Editar",
        ["os.camera_save"] = "Guardar",
        ["os.camera_use"] = "Usar foto",
    };

    public static readonly Dictionary<string, string> Fr = new()
    {
        ["os.camera_hint"] = "La photo se cadre en jeu ; le téléphone se masque pendant la visée. Faites glisser le cadre pour l'ajuster.",
        ["os.camera_roll"] = "Pellicule",
        ["os.camera_empty"] = "Pas encore de photos. Prenez votre premier cliché !",
        ["os.camera_for_app"] = "Photo demandée par {0}",
        ["os.camera_retake"] = "Reprendre",
        ["os.camera_cancel"] = "Annuler",
        ["os.camera_delete_confirm"] = "Supprimer cette photo ? Cette action est irréversible.",
        ["os.camera_delete"] = "Supprimer",
        ["os.camera_review_title"] = "La photo vous plaît ?",
        ["os.camera_edit"] = "Modifier",
        ["os.camera_save"] = "Enregistrer",
        ["os.camera_use"] = "Utiliser la photo",
    };

    public static readonly Dictionary<string, string> Pt = new()
    {
        ["os.camera_hint"] = "A foto é enquadrada no jogo; o telefone some enquanto você mira. Arraste o quadro para ajustar.",
        ["os.camera_roll"] = "Rolo da câmera",
        ["os.camera_empty"] = "Nenhuma foto ainda. Tire a sua primeira!",
        ["os.camera_for_app"] = "Foto solicitada por {0}",
        ["os.camera_retake"] = "Tirar de novo",
        ["os.camera_cancel"] = "Cancelar",
        ["os.camera_delete_confirm"] = "Excluir esta foto? Isso não pode ser desfeito.",
        ["os.camera_delete"] = "Excluir",
        ["os.camera_review_title"] = "Gostou da foto?",
        ["os.camera_edit"] = "Editar",
        ["os.camera_save"] = "Salvar",
        ["os.camera_use"] = "Usar foto",
    };

    public static readonly Dictionary<string, string> Ru = new()
    {
        ["os.camera_hint"] = "Кадр выбирается прямо в игре; телефон скрывается, пока вы целитесь. Перетащите рамку, чтобы подогнать кадр.",
        ["os.camera_roll"] = "Галерея снимков",
        ["os.camera_empty"] = "Пока нет фотографий. Сделайте первый снимок!",
        ["os.camera_for_app"] = "Фото запросило приложение {0}",
        ["os.camera_retake"] = "Переснять",
        ["os.camera_cancel"] = "Отмена",
        ["os.camera_delete_confirm"] = "Удалить это фото? Действие нельзя отменить.",
        ["os.camera_delete"] = "Удалить",
        ["os.camera_review_title"] = "Нравится снимок?",
        ["os.camera_edit"] = "Редактировать",
        ["os.camera_save"] = "Сохранить",
        ["os.camera_use"] = "Использовать",
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Packs =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en"] = En,
            ["de"] = De,
            ["es"] = Es,
            ["fr"] = Fr,
            ["pt"] = Pt,
            ["ru"] = Ru,
        };
}
