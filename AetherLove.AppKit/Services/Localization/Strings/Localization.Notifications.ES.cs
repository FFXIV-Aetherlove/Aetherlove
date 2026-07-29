namespace AetherLove.Services.Localization;

internal static class NotificationsEs
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["notif.new_message"] = "Tienes un mensaje nuevo.",
        ["notif.match_title"] = "¡Nuevo match!",
        ["notif.matched_with_popup"] = "Tienes match con {0}.",
        ["notif.matched_with_chat"] = "¡Tienes match con {0}!",
        ["notif.someone_new"] = "alguien nuevo",
        ["notif.open_messages"] = "Abrir mensajes",
        ["notif.pulse_link"] = "Abrir AetherLove",

        // Messenger (added after update 1.5.1)
        ["notif.msgr_message"] = "Has recibido un mensaje de Messenger.",
        ["notif.market_below"] = "{0} bajó a {1}",
        ["notif.market_above"] = "{0} subió a {1}",
        ["notif.msgr_request"] = "{0} quiere añadirte en Messenger.",
        ["notif.msgr_open_link"] = "Abrir Messenger",
        ["notif.msgr_request_title"] = "Solicitud de Messenger",
        ["notif.msgr_message_body"] = "Mensaje nuevo",
        ["notif.msgr_group_added"] = "Te añadieron a \"{0}\"",
        ["notif.msgr_keys_reset"] = "{0} ha restablecido sus claves de cifrado E2E",

        // Clock timers (added after update 2.0.0.0)
        ["notif.clock_chat"] = "El temporizador \"{0}\" ha finalizado.",
        ["notif.clock_notif_title"] = "Temporizador finalizado",
        ["notif.clock_notif_body"] = "\"{0}\" ya está.",
        ["notif.clock_untitled"] = "Temporizador",
        ["notif.clock_open"] = "Abrir",

        // Chat notification link (added after update 2.0.0.0)
        ["notif.view_link"] = "Ver",
    };
}
