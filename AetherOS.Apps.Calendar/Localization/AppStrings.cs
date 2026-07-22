using System.Collections.Generic;

namespace AetherOS.Apps.Calendar;

internal static class AppStrings
{
    public static readonly Dictionary<string, string> En = new()
    {
        ["os.cal_today"] = "Today",
        ["os.cal_add"] = "New event",
        ["os.cal_event_title"] = "Title",
        ["os.cal_event_note"] = "Note (optional)",
        ["os.cal_time"] = "Time",
        ["os.cal_empty"] = "Nothing planned for this day.",
        ["os.cal_delete_confirm"] = "Delete this event? This cannot be undone.",
    };

    public static readonly Dictionary<string, string> De = new()
    {
        ["os.cal_today"] = "Heute",
        ["os.cal_add"] = "Neuer Termin",
        ["os.cal_event_title"] = "Titel",
        ["os.cal_event_note"] = "Notiz (optional)",
        ["os.cal_time"] = "Uhrzeit",
        ["os.cal_empty"] = "Für diesen Tag ist nichts geplant.",
        ["os.cal_delete_confirm"] = "Diesen Termin löschen? Das kann nicht rückgängig gemacht werden.",
    };

    public static readonly Dictionary<string, string> Es = new()
    {
        ["os.cal_today"] = "Hoy",
        ["os.cal_add"] = "Nuevo evento",
        ["os.cal_event_title"] = "Título",
        ["os.cal_event_note"] = "Nota (opcional)",
        ["os.cal_time"] = "Hora",
        ["os.cal_empty"] = "No hay nada planeado para este día.",
        ["os.cal_delete_confirm"] = "¿Eliminar este evento? No se puede deshacer.",
    };

    public static readonly Dictionary<string, string> Fr = new()
    {
        ["os.cal_today"] = "Aujourd'hui",
        ["os.cal_add"] = "Nouvel événement",
        ["os.cal_event_title"] = "Titre",
        ["os.cal_event_note"] = "Note (facultative)",
        ["os.cal_time"] = "Heure",
        ["os.cal_empty"] = "Rien de prévu ce jour-là.",
        ["os.cal_delete_confirm"] = "Supprimer cet événement ? Cette action est irréversible.",
    };

    public static readonly Dictionary<string, string> Pt = new()
    {
        ["os.cal_today"] = "Hoje",
        ["os.cal_add"] = "Novo evento",
        ["os.cal_event_title"] = "Título",
        ["os.cal_event_note"] = "Nota (opcional)",
        ["os.cal_time"] = "Hora",
        ["os.cal_empty"] = "Nada planejado para este dia.",
        ["os.cal_delete_confirm"] = "Excluir este evento? Isso não pode ser desfeito.",
    };

    public static readonly Dictionary<string, string> Ru = new()
    {
        ["os.cal_today"] = "Сегодня",
        ["os.cal_add"] = "Новое событие",
        ["os.cal_event_title"] = "Название",
        ["os.cal_event_note"] = "Заметка (необязательно)",
        ["os.cal_time"] = "Время",
        ["os.cal_empty"] = "На этот день ничего не запланировано.",
        ["os.cal_delete_confirm"] = "Удалить это событие? Действие нельзя отменить.",
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
