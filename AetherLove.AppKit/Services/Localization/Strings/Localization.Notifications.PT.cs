namespace AetherLove.Services.Localization;

internal static class NotificationsPt
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["notif.new_message"] = "Tens uma nova mensagem.",
        ["notif.match_title"] = "Novo match!",
        ["notif.matched_with_popup"] = "Tens match com {0}.",
        ["notif.matched_with_chat"] = "Tens match com {0}!",
        ["notif.someone_new"] = "alguém novo",
        ["notif.open_messages"] = "Abrir mensagens",
        ["notif.pulse_link"] = "Abrir AetherLove",

        // Messenger (added after update 1.5.1)
        ["notif.msgr_message"] = "Você recebeu uma mensagem no Messenger.",
        ["notif.market_below"] = "{0} caiu para {1}",
        ["notif.market_above"] = "{0} subiu para {1}",
        ["notif.msgr_request"] = "{0} quer adicionar você no Messenger.",
        ["notif.msgr_open_link"] = "Abrir Messenger",
        ["notif.msgr_request_title"] = "Solicitação do Messenger",
        ["notif.msgr_message_body"] = "Nova mensagem",
        ["notif.msgr_group_added"] = "Você foi adicionado a \"{0}\"",
        ["notif.msgr_keys_reset"] = "{0} redefiniu as chaves de criptografia E2E",

        // Clock timers (added after update 2.0.0.0)
        ["notif.clock_chat"] = "O timer \"{0}\" terminou.",
        ["notif.clock_notif_title"] = "Timer terminado",
        ["notif.clock_notif_body"] = "\"{0}\" acabou.",
        ["notif.clock_untitled"] = "Timer",
        ["notif.clock_open"] = "Abrir",

        // Chat notification link (added after update 2.0.0.0)
        ["notif.view_link"] = "Ver",

        // added after update 2.1.3
        ["notif.realtor_accepting"] = "A loteria de casas já aceita inscrições. Veja os lotes livres no app Imobiliária.",
        ["notif.realtor_results"] = "A loteria de casas entrou no período de resultados. Confira como você se saiu no app Imobiliária.",
        ["notif.staff_notice_title"] = "Mensagem da equipe",
        ["notif.staff_notice_body"] = "Abra as Configurações para ler.",
    };
}
