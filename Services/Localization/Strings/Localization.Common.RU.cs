namespace AetherLove.Services.Localization;

internal static class CommonRu
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["common.ok"] = "ОК",
        ["common.confirm"] = "Подтвердить",
        ["common.cancel"] = "Отмена",
        ["common.loading"] = "Загрузка…",
        ["common.try_again"] = "Попробовать снова",
        ["common.i_understand"] = "Понятно",
        ["common.sign_out"] = "Выйти",
        ["common.got_it"] = "Понятно!",
        ["common.moderator_notes_label"] = "Примечания от модераторов",
        ["common.server_unreachable_detail"] = "Не удалось подключиться к серверу: {0}",

        ["common.banned_title"] = "Аккаунт заблокирован",
        ["common.banned_body"] = "Ваш аккаунт AetherLove был заблокирован. Вы больше не можете пользоваться сервисом.",
        ["common.banned_reason_label"] = "Причина",
        ["common.banned_uninstall_hint"] = "Вы можете закрыть это окно и удалить плагин в любое время.",

        // Outdated-plugin screen
        ["common.outdated_title"] = "Требуется обновление",
        ["common.outdated_body"] = "Вы используете устаревшую версию AetherLove. Сервер больше не поддерживает эту версию, поэтому плагин не может подключиться.",
        ["common.outdated_hint"] = "Обновите плагин в установщике плагинов Dalamud, затем снова откройте AetherLove.",

        ["common.offline_title"] = "AetherLove - не в сети",
        ["common.offline_body"] = "Нет соединения серверами AetherLove. Приложению нужно активное соединение, чтобы просматривать, совпадать и общаться, поэтому оно приостановлено, пока мы не вернёмся в сеть.",
        ["common.offline_reconnecting"] = "Повторное подключение…",
        ["common.offline_keep_trying"] = "Мы будем продолжать попытки автоматически.",

        ["common.passphrase_title"] = "Введите вашу кодовую фразу-пароль",
        ["common.passphrase_intro"] = "Мы узнаём этот аккаунт, но на этом устройстве ещё нет вашего ключа чата. Введите кодовую фразу, которую вы задали на первом устройстве, чтобы разблокировать историю чатов.",
        ["common.passphrase_forgot"] = "Забыли кодовую фразу? Аккаунт восстановлению не подлежит, но вы можете выйти ниже и создать новый аккаунт. История чатов с этим аккаунтом будет потеряна.",
        ["common.passphrase_bundle_load_failed"] = "Не удалось расшифровать данные с сервера.",
        ["common.passphrase_empty"] = "Пожалуйста, введите кодовую фразу.",
        ["common.passphrase_incorrect"] = "Неверная кодовая фраза. Попробуйте снова.",
        ["common.passphrase_unlock_failed"] = "Не удалось разблокировать: {0}",
        ["common.unlock"] = "Разблокировать",
        ["common.unlocking"] = "Разблокировка…",

        ["common.warnings_heading_one"] = "У вас есть предупреждение от модератора",
        ["common.warnings_heading_many"] = "У вас {0} предупреждений от модераторов",
        ["common.warnings_body"] = "Пожалуйста, прочитайте следующее предупреждение(я) от команды модераторов. Повторные нарушения могут привести к приостановке аккаунта.",
        ["common.warnings_submit_error"] = "Не удалось подключиться к серверу: {0}. Нажмите, чтобы повторить.",
        ["common.acknowledging"] = "Подтверждение…",

        ["common.nsfw_decl_unselected"] = "выберите вариант ниже",
        ["common.nsfw_decl_sfw"] = "это изображение - SFW",
        ["common.nsfw_decl_nsfw"] = "это изображение - NSFW",
        ["common.lalafell_nsfw_title"] = "NSFW недоступно",
        ["common.lalafell_nsfw_body"] = "Мы не разрешаем NSFW-изображения персонажам-лалафелям. Поскольку лалафели выглядят по-детски, мы применяем эту политику единообразно ко всем аккаунтам с персонажем-лалафелем и не делаем исключений в индивидуальном порядке.\n\nВаше фото возвращено к статусу SFW. Если это фото не safe-for-work, пожалуйста, удалите его и загрузите другое.",
        ["common.undeclared_photo_title"] = "Требуется отметка",
        ["common.undeclared_photo_body"] = "Прежде чем загрузить ещё одно фото, вы должны выбрать в поле выбора, является ли ваше другое изображение SFW или NSFW.",

        ["common.changelog_window_title"] = "AetherLove — Что нового",
        ["common.whats_new"] = "Что нового",
        ["common.changelog_empty"] = "Записей в списке изменений нет.",
        ["common.changelog_latest"] = "Последнее",
        ["common.changelog_important"] = "Важное",
        ["common.changelog_new_features"] = "Новые возможности",
        ["common.changelog_bug_fixes"] = "Исправления багов",

        ["common.rate_limit_title"] = "Не так быстро",
        ["common.rate_limit_noun_profile"] = "профиль",
        ["common.rate_limit_noun_images"] = "изображения",
        ["common.rate_limit_body"] = "Вы можете менять свой {0} только {1} раз в час. Пожалуйста, попробуйте снова через {2}.",
        ["common.rate_limit_retry_moment"] = "мгновение",
        ["common.rate_limit_retry_one_second"] = "1 секунду",
        ["common.rate_limit_retry_seconds"] = "{0} секунд",
        ["common.rate_limit_retry_one_minute"] = "1 минуту",
        ["common.rate_limit_retry_minutes"] = "{0} минут",

        // Emoji picker
        ["common.emoji_search_hint"] = "Поиск эмодзи...",
        ["common.emoji_none_found"] = "Эмодзи не найдено.",

        // Bottom navigation bar
        ["common.nav_swipe"] = "Анкеты",
        ["common.nav_matches"] = "Пары",
        ["common.nav_settings"] = "Настр.",
        ["common.nav_minimize"] = "Свернуть",

        // Close-plugin confirmation modal
        ["common.close_plugin_tooltip"] = "Закрыть AetherLove",
        ["common.close_plugin_title"] = "Закрыть AetherLove?",
        ["common.close_plugin_body"] = "Данная кнопка только скроет окно. Вы останетесь в сети и будете получать уведомления о новых парах и сообщения, пока плагин включён.\n\nОткройте окно снова в любой момент, введя {0} в чат.",
        ["common.close_plugin_tip"] = "Совет: используйте кнопку «Свернуть» внизу, чтобы маленький виджет уведомлений оставался видимым.",
        ["common.close"] = "Закрыть",

        // Save-error modal
        ["common.save_error_title"] = "Что-то пошло не так",
        ["common.save_error_intro"] = "Не удалось сохранить изменения:",
        ["common.save_error_report"] = "Если это повторяется, сообщите об ошибке на нашем Discord.",
        ["common.save_error_unknown"] = "Произошла непредвиденная ошибка.",

        // Image requirements modal
        ["common.img_requirements_title"] = "Изображение нельзя использовать",
        ["common.img_invalid"] = "Этот файл не является корректным изображением или его формат не поддерживается.",
        ["common.img_too_small"] = "Это изображение всего {0}×{1} пикс. — оно слишком маленькое.",
        ["common.img_requirements_sizes"] = "Для аватаров нужно не менее {0}×{1} пикс., а для фото профиля — не менее {2}×{3} пикс. Выберите изображение покрупнее.",

        // Image crop window
        ["common.loading_image"] = "Загрузка изображения...",
        ["common.use_this_crop"] = "Использовать этот кадр",
    };
}
