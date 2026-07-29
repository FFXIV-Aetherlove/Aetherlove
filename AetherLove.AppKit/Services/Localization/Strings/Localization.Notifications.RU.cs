namespace AetherLove.Services.Localization;

internal static class NotificationsRu
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["notif.new_message"] = "Входящее сообщение.",
        ["notif.match_title"] = "Новая пара!",
        ["notif.matched_with_popup"] = "У вас пара с {0}.",
        ["notif.matched_with_chat"] = "У вас пара с {0}!",
        ["notif.someone_new"] = "кто-то новенький",
        ["notif.open_messages"] = "Открыть сообщения",
        ["notif.pulse_link"] = "Открыть AetherLove",

        // Messenger (added after update 1.5.1)
        ["notif.msgr_message"] = "Вам пришло сообщение в Messenger.",
        ["notif.market_below"] = "{0} подешевел до {1}",
        ["notif.market_above"] = "{0} подорожал до {1}",
        ["notif.msgr_request"] = "{0} хочет добавить вас в Messenger.",
        ["notif.msgr_open_link"] = "Открыть Messenger",
        ["notif.msgr_request_title"] = "Заявка в Messenger",
        ["notif.msgr_message_body"] = "Новое сообщение",
        ["notif.msgr_group_added"] = "Вас добавили в \"{0}\"",
        ["notif.msgr_keys_reset"] = "{0} сбросил(а) свои ключи сквозного шифрования",

        // Clock timers (added after update 2.0.0.0)
        ["notif.clock_chat"] = "Таймер \"{0}\" сработал.",
        ["notif.clock_notif_title"] = "Таймер сработал",
        ["notif.clock_notif_body"] = "\"{0}\" готово.",
        ["notif.clock_untitled"] = "Таймер",
        ["notif.clock_open"] = "Открыть",

        // Chat notification link (added after update 2.0.0.0)
        ["notif.view_link"] = "Открыть",
    };
}
