namespace AetherLove.Services.Localization;

internal static class SettingsRu
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["settings.title"] = "Настройки",

        ["settings.section_appearance"] = "Внешний вид",
        ["settings.section_phone_size"] = "Размер телефона",
        ["settings.section_plugin_language"] = "Язык плагина",
        ["settings.section_privacy"] = "Конфиденциальность",
        ["settings.section_general"] = "Общие",
        ["settings.section_notifications"] = "Уведомления",
        ["settings.section_moderation"] = "Уведомления от администрации",

        ["settings.phone_size_small"] = "Маленький",
        ["settings.phone_size_medium"] = "Средний",
        ["settings.phone_size_large"] = "Большой",
        ["settings.phone_size_caption"] = "Масштабирует весь телефон. Большие размеры подходят для экранов с высоким разрешением; «Большой» может не поместиться на дисплее 1080p.",

        ["settings.disable_startup_heartbeat"] = "Отключить звук сердцебиения при запуске",

        ["settings.view_changelog"] = "Просмотреть список изменений",
        ["settings.send_feedback"] = "Отправить отзыв",
        ["settings.delete_account"] = "Удалить аккаунт",
        ["settings.create_new_profile"] = "Создать новый профиль",
        ["settings.cancel"] = "Отмена",
        ["settings.back"] = "Назад",

        ["settings.always_blur_nsfw"] = "Всегда размывать NSFW",
        ["settings.always_blur_nsfw_tooltip"] = "Когда включено, NSFW-помеченные дополнительные фото в других профилях размыты, пока вы не нажмёте, чтобы показать каждое. Аватары и главные портреты всегда safe-for-work независимо от этого. Отключение показывает каждое фото как есть.",
        ["settings.nsfw_profile"] = "Мой профиль NSFW (18+)",
        ["settings.nsfw_profile_tooltip"] = "Помечает ваш профиль как для взрослых/NSFW, чтобы его видели только те, у кого включён NSFW. Включается автоматически, когда вы добавляете NSFW-фото или выбираете ролевую игру 18+, и остаётся включённым, пока вы их не уберёте.",
        ["settings.nsfw_profile_locked"] = "Вы не можете отключить это, пока у вас есть NSFW-фото или выбрана ролевая игра 18+ (ERP). Сначала удалите свои NSFW-изображения и снимите выбор ролевой игры 18+.",

        ["settings.enable_notifications"] = "Включить уведомления",
        ["settings.enable_notifications_tooltip"] = "Главный параметр от всех уведомлений. Отключите, чтобы заглушить все игровые объявления в чате, всплывающие окна и звуки ниже.",
        ["settings.enable_notification_sounds"] = "Включить звуки уведомлений",
        ["settings.enable_notification_sounds_tooltip"] = "Звуки уведомлений будут воспроизводиться, только если звук игры и звук спецэффектов не отключены. Громкость регулируется через громкость Windows.",
        ["settings.announce_messages_chat"] = "Объявлять о новых сообщениях в игровом чате",
        ["settings.announce_matches_chat"] = "Объявлять о новых совпадениях в игровом чате",
        ["settings.popup_messages"] = "Показывать всплывающее окно для новых сообщений",
        ["settings.popup_matches"] = "Показывать всплывающее окно для новых совпадений",
        ["settings.auto_open_minimized"] = "Открывать свёрнутым автоматически при входе в игру",
        ["settings.pulse_optout"] = "Иногда сообщения в игровом чате",
        ["settings.pulse_optout_tooltip"] = "Время от времени AetherLove может оставлять шутливое сообщение в игровом чате. Отключите, чтобы прекратить.",
        ["settings.combat_behavior"] = "В бою",
        ["settings.combat_behavior_hide"] = "Скрыть AetherLove",
        ["settings.combat_behavior_minimize"] = "Свернуть в виджет",
        ["settings.combat_behavior_leave_open"] = "Оставить открытым",
        ["settings.notification_sound"] = "Звук уведомления",
        ["settings.play"] = "Воспроизвести",

        ["settings.delete_warning_intro"] = "Это действие необратимо и не может быть отменено. Ознакомьтесь со следующим, прежде чем продолжить:",
        ["settings.delete_bullet_account"] = "Ваш аккаунт будет навсегда удалён.",
        ["settings.delete_bullet_matches"] = "Все ваши пары будут удалены.",
        ["settings.delete_bullet_preferences"] = "Ваши предпочтения для пар будут удалены.",
        ["settings.delete_bullet_pictures"] = "Ваши фотографии профиля будут удалены.",
        ["settings.delete_reregister"] = "Вы всегда можете зарегистрироваться заново в любой момент.",
        ["settings.delete_previous_failed"] = "Предыдущая попытка не удалась: {0}",

        ["settings.deleting_title"] = "Удаление аккаунта",
        ["settings.deleting_body"] = "Удаление ваших данных и отказ от пар с контактами",
        ["settings.deleted_title"] = "Аккаунт удалён",
        ["settings.deleted_body"] = "Ваш аккаунт удалён, ваши данные и фотографии удалены, а ваши пары отменены. Теперь вы можете удалить плагин или пройти регистрацию и создать новый профиль.",

        ["settings.warnings_button_unseen"] = "Предупреждения ({0} непросмотренных: / {1})",
        ["settings.warnings_button"] = "Предупреждения ({0})",
        ["settings.warnings_title"] = "Предупреждения",
        ["settings.no_warnings"] = "Предупреждений нет.",
        ["settings.back_to_settings_arrow"] = "← Назад к настройкам",

        ["settings.back_to_settings"] = "Назад к настройкам",
        ["settings.feedback_thanks"] = "Спасибо! Ваш отзыв отправлен команде AetherLove.",
        ["settings.feedback_intro"] = "Нашли баг, есть идея или хотите что-то предложить? Дайте нам знать.",
        ["settings.feedback_note"] = "Обратите внимание: отзыв нельзя использовать для обжалования бана или предупреждения.",
        ["settings.feedback_type"] = "Тип",
        ["settings.feedback_kind_bug"] = "Баг",
        ["settings.feedback_kind_improvement"] = "Улучшение",
        ["settings.feedback_kind_other"] = "Другое",
        ["settings.feedback_your_message"] = "Ваше сообщение",
        ["settings.sending"] = "Отправка…",
        ["settings.submit"] = "Отправить",
        ["settings.feedback_rate_limited"] = "Вы можете отправлять отзыв только {0} раз в час. Пожалуйста, попробуйте позже.",
        ["settings.feedback_send_failed"] = "Не удалось отправить ваш отзыв. Пожалуйста, попробуйте снова.",
    };
}
