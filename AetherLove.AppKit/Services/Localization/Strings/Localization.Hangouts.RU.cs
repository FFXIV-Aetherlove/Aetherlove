namespace AetherLove.Services.Localization;

internal static class HangoutsRu
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        // added after update 1.5.1
        ["common.nav_hangouts"] = "Встречи",
        ["chat.preview_hangout"] = "Поделился встречей",
        ["hangout.starts_at"] = "Начало в {0}",
        ["hangout.ends_in"] = "Закончится через {0}",
        ["hangout.ended"] = "Завершена",
        ["hangout.coming_count"] = "Идут: {0}",
        ["hangout.chip_live"] = "LIVE",
        ["hangout.menu_view"] = "Открыть встречу",
        ["hangout.notif_rsvp"] = "{0} направляется на вашу встречу!",
        ["hangout.notif_rsvp_link"] = "Открыть встречу",
        ["hangout.notif_cancelled"] = "Встреча, на которую вы записались, отменена.",
        ["hangout.notif_ended_early"] = "Встреча, на которую вы записались, завершилась раньше.",
        ["hangout.notif_browse_link"] = "Смотреть встречи",
        ["hangout.notif_friend_started"] = "Ваш друг {0} начал(а) встречу!",
        ["hangout.notif_friend_title"] = "Встреча началась!",
        ["hangout.share_view"] = "Открыть эту встречу",
        ["hangout.share_unavailable"] = "Эта встреча уже завершилась",
        ["hangout.cat_pve"] = "PvE",
        ["hangout.cat_pvp"] = "PvP",
        ["hangout.cat_rp"] = "Ролевая игра",
        ["hangout.cat_social"] = "Общение",
        ["hangout.cat_fishing"] = "Рыбалка",
        ["hangout.cat_gposing"] = "GPose",
        ["hangout.cat_goldsaucer"] = "Голд Сойсер",
        ["hangout.cat_deepdungeon"] = "Дип Данжен",
        ["hangout.cat_mahjong"] = "Маджонг",
        ["hangout.cat_tripletriad"] = "Triple Triad",
        ["hangout.cat_treasure"] = "Охота за сокровищами",
        ["hangout.cat_fates"] = "FATE",
        ["huberror.hangouts_disabled"] = "Встречи сейчас недоступны.",
        ["huberror.hangout_not_found"] = "Эта встреча уже завершилась или недоступна.",
        ["huberror.hangout_already_active"] = "У вас уже есть активная встреча. Завершите её, прежде чем начинать новую.",
        ["huberror.hangout_rsvp_own"] = "Нельзя записаться на собственную встречу.",
        ["huberror.hangout_times_invalid"] = "Начало или длительность вне допустимых пределов.",
        ["huberror.hangout_description_too_long"] = "Описание превышает лимит в {0} символов.",

        // added after update 2.0.0 (promoted from the Hangouts app pack for the messenger popup)
        ["hangout.status_live"] = "СЕЙЧАС",
        ["hangout.status_upcoming"] = "Скоро",
        ["hangout.hosted_by"] = "Организует {0}",
        ["hangout.copy_address"] = "Скопировать адрес",
        ["hangout.copied"] = "Скопировано!",
        ["hangout.at_capacity"] = "(мест нет)",
        ["hangout.on_my_way"] = "Записаться",
        ["hangout.on_my_way_undo"] = "Отменить запись",
        ["hangout.report_title"] = "Пожаловаться на встречу",
        ["hangout.report_body"] = "Расскажите, что не так с этой встречей (реклама заведения, платные услуги, NSFW-контент...).",
        ["hangout.report_submit"] = "Отправить жалобу",
        ["hangout.report_thanks"] = "Спасибо. Наша команда всё проверит.",
    };
}
