namespace AetherLove.Services.Localization;

internal static class CommonRu
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["common.ok"] = "ОК",
        ["common.cancel"] = "Отмена",
        ["common.loading"] = "Загрузка…",
        ["common.try_again"] = "Попробовать снова",
        ["common.i_understand"] = "Понятно",
        ["common.sign_out"] = "Выйти",
        ["common.got_it"] = "Понятно!",
        ["common.server_unreachable_detail"] = "Не удалось подключиться к серверу: {0}",

        ["common.banned_title"] = "Профиль заблокирован",
        ["common.banned_body"] = "Заблокированный профиль AetherLove означает, что вы больше не можете пользоваться AetherLove с этим профилем. Остальные наши приложения по-прежнему доступны. За подробностями откройте тикет в поддержку на нашем Discord.",
        ["common.banned_reason_label"] = "Причина",
        ["common.banned_uninstall_hint"] = "Нажмите кнопку «Домой» внизу, чтобы вернуться на главный экран.",

        // Outdated-plugin screen
        ["common.outdated_title"] = "Требуется обновление",
        ["common.outdated_body"] = "Вы используете устаревшую версию AetherLove. Сервер больше не поддерживает эту версию, поэтому плагин не может подключиться.",
        ["common.outdated_hint"] = "Обновите плагин в установщике плагинов Dalamud, затем снова откройте AetherLove.",

        ["common.offline_title"] = "Сервисы AetherOS сейчас не в сети",
        ["common.offline_body"] = "Сервер, скорее всего, недоступен из-за обновления или технических работ. Восстановление соединения не займёт больше пары минут!",
        ["common.offline_reconnecting"] = "Повторное подключение…",
        ["common.offline_taking_long"] = "Это занимает больше времени, чем обычно. Посетите наш Discord-сервер, чтобы узнать актуальный статус.",
        ["common.offline_join_discord"] = "Перейти в Discord",

        ["common.passphrase_title"] = "Введите вашу кодовую фразу-пароль",
        ["common.passphrase_intro"] = "Мы узнаём этот аккаунт, но на этом устройстве ещё нет вашего ключа чата. Введите кодовую фразу, которую вы задали на первом устройстве, чтобы разблокировать историю чатов.",
        ["common.passphrase_forgot"] = "Забыли кодовую фразу? Ниже можно сбросить ключи шифрования. Всё, что было отправлено до сброса, станет для вас нечитаемым.",

        // Passphrase reset (added after update 1.5.1)
        ["common.passphrase_reset_button"] = "Сбросить ключи шифрования…",
        ["common.passphrase_reset_title"] = "Сброс ключей шифрования",
        ["common.passphrase_reset_warning"] = "Будут созданы совершенно новая кодовая фраза и новые ключи шифрования. Вы НАВСЕГДА потеряете доступ ко всем сообщениям, отправленным до сброса, а ваши пары и контакты в Messenger увидят уведомление о том, что вы сбросили ключи.",
        ["common.passphrase_reset_new"] = "Новая кодовая фраза",
        ["common.passphrase_reset_repeat"] = "Повторите новую кодовую фразу",
        ["common.passphrase_reset_mismatch"] = "Кодовые фразы не совпадают.",
        ["common.passphrase_reset_go"] = "Сбросить мои ключи",
        ["common.passphrase_reset_running"] = "Сброс…",
        ["common.passphrase_bundle_load_failed"] = "Не удалось расшифровать данные с сервера.",
        ["common.passphrase_empty"] = "Пожалуйста, введите кодовую фразу.",
        ["common.passphrase_incorrect"] = "Неверная кодовая фраза. Попробуйте снова.",
        ["common.passphrase_unlock_failed"] = "Не удалось разблокировать: {0}",
        ["common.unlock"] = "Разблокировать",
        ["common.unlocking"] = "Разблокировка…",

        // Encryption recovery screen
        ["common.recovery_title"] = "Настройка защищённых сообщений",
        ["common.recovery_intro"] = "В вашем аккаунте не установлены ключи шифрования, поэтому вы пока не можете отправлять и получать сообщения. Придумайте фразу-пароль, чтобы создать их. Сохраните её надёжно, восстановить её невозможно.",
        ["common.recovery_button"] = "Включить защищённые сообщения",
        ["common.recovery_support"] = "Всё ещё не получается? Выйдите из аккаунта ниже или свяжитесь с нами в Discord.",

        ["common.warnings_heading_one"] = "У вас есть предупреждение от модератора",
        ["common.warnings_heading_many"] = "У вас {0} предупреждений от модераторов",
        ["common.warnings_body"] = "Пожалуйста, прочитайте следующее предупреждение(я) от команды модераторов. Повторные нарушения могут привести к приостановке аккаунта.",
        ["common.warnings_submit_error"] = "Не удалось подключиться к серверу: {0}. Нажмите, чтобы повторить.",
        ["common.acknowledging"] = "Подтверждение…",

        // Moderator message screen
        ["common.modmsg_heading_one"] = "У вас есть сообщение от команды модерации",
        ["common.modmsg_heading_many"] = "У вас есть сообщения от команды модерации: {0}",
        ["common.modmsg_body"] = "Модератор отправил(-а) вам следующее сообщение:",
        ["common.modmsg_got_it"] = "Понятно",

        ["common.nsfw_decl_unselected"] = "выберите вариант ниже",
        ["common.nsfw_decl_sfw"] = "это изображение - SFW",
        ["common.nsfw_decl_nsfw"] = "это изображение - NSFW",
        ["common.lalafell_nsfw_title"] = "NSFW недоступно",
        ["common.lalafell_nsfw_body"] = "Мы не разрешаем NSFW-изображения персонажам-лалафелям. Поскольку лалафели выглядят по-детски, мы применяем эту политику единообразно ко всем аккаунтам с персонажем-лалафелем и не делаем исключений в индивидуальном порядке.\n\nВаше фото возвращено к статусу SFW. Если это фото не safe-for-work, пожалуйста, удалите его и загрузите другое.",
        ["common.undeclared_photo_title"] = "Требуется отметка",
        ["common.undeclared_photo_body"] = "Прежде чем загрузить ещё одно фото, вы должны выбрать в поле выбора, является ли ваше другое изображение SFW или NSFW.",

        ["common.changelog_window_title"] = "AetherLove: Что нового",
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

        // Close-plugin confirmation modal
        ["common.close_plugin_tooltip"] = "Закрыть AetherOS",
        ["common.minimize_tooltip"] = "Свернуть AetherOS",
        ["common.close_plugin_title"] = "Закрыть AetherLove?",
        ["common.close_plugin_body"] = "Данная кнопка только скроет окно. Вы останетесь в сети и будете получать уведомления о новых парах и сообщения, пока плагин включён.\n\nОткройте окно снова в любой момент, введя {0} в чат.",
        ["common.close_plugin_tip"] = "Совет: используйте кнопку «Свернуть» внизу, чтобы маленький виджет уведомлений оставался видимым.",
        ["common.close_plugin_dont_ask"] = "Больше не показывать это окно",
        ["common.close"] = "Закрыть",

        // Save-error modal
        ["common.save_error_title"] = "Что-то пошло не так",
        ["common.save_error_intro"] = "Не удалось сохранить изменения:",
        ["common.save_error_report"] = "Если это повторяется, сообщите об ошибке на нашем Discord.",
        ["common.save_error_unknown"] = "Произошла непредвиденная ошибка.",

        // Image requirements modal
        ["common.img_requirements_title"] = "Изображение нельзя использовать",
        ["common.img_invalid"] = "Этот файл не является корректным изображением или его формат не поддерживается.",
        ["common.img_too_small"] = "Это изображение всего {0}×{1} пикс. Оно слишком маленькое.",
        ["common.img_requirements_sizes"] = "Для аватаров нужно не менее {0}×{1} пикс., а для фото профиля нужно не менее {2}×{3} пикс. Выберите изображение покрупнее.",

        // Image crop window
        ["common.loading_image"] = "Загрузка изображения...",
        ["common.use_this_crop"] = "Использовать этот кадр",

        // SFW-image gate modal (main avatar + first profile photo must be SFW)
        ["common.sfw_gate_title"] = "Профиль + Аватар - ТОЛЬКО SFW",
        ["common.sfw_gate_subtitle"] = "Что НЕ является SFW:",
        ["common.sfw_gate_b1"] = "Полная нагота любого пола.",
        ["common.sfw_gate_b2"] = "Видимые соски груди любого пола.",
        ["common.sfw_gate_b3"] = "Видимые лобковые волосы или области гениталий.",
        ["common.sfw_gate_b4"] = "Натуралистичные/реалистичные изображения крови, травм, ран или телесных повреждений.",
        ["common.sfw_gate_b5"] = "Татуировки, знаки, символы или текст, которые являются непристойными, дискриминационными, разжигающими ненависть или направлены против отдельных лиц или групп по признаку расы, этнической принадлежности, национальности, религии, пола, сексуальной ориентации или иных защищённых признаков.",
        ["common.sfw_gate_b6"] = "Сексуальные жесты, позы или визуальные отсылки, которые подразумевают или имитируют половые акты, включая оральный секс, мастурбацию или иную сексуальную активность.",
        ["common.sfw_gate_secondary"] = "Материалы NSFW по-прежнему можно загружать во второстепенные изображения профиля.",
        ["common.sfw_gate_ack"] = "Я понимаю правила SFW",

        // added after update (1.3.1)
        ["common.sfw_gate_race_gender"] = "Пожалуйста, убедитесь, что на вашем главном изображении раса и пол персонажа совпадают с указанными в профиле.",

        // added after update 1.4.3
        ["common.img_cloud_title"] = "Файл не загружен",
        ["common.img_cloud_unavailable"] = "Это изображение находится в директории облачного приложения (например, OneDrive/Google Диск) и не загружено на ваш компьютер, поэтому его нельзя открыть. В проводнике щёлкните по нему правой кнопкой мыши, выберите 'Всегда хранить на этом устройстве', дождитесь зелёной галочки и попробуйте снова. Либо выберите файл, сохранённый локально на компьютере.",
        ["common.emoji_favorites"] = "Избранное",
        ["common.emoji_favorite_hint"] = "правый клик, чтобы добавить или убрать из избранного",
        ["common.emoji_add_favorite"] = "Добавить в избранное",
        ["common.emoji_remove_favorite"] = "Убрать из избранного",
        ["common.selfie"] = "Селфи",
        ["common.selfie_instructions"] = "Перетащите или измените размер рамки на персонаже и сделайте снимок.",
        ["common.selfie_take"] = "Сделать фото",
        ["common.selfie_capturing"] = "Идет съёмка...",
        ["common.offline_maintenance"] = "Сервер на техническом обслуживании.",

        // added after update 1.5.0
        ["common.nav_places"] = "Места",

        // Multi-profile switch nav slot (added after update 1.5.1)
        ["common.nav_switch"] = "Сменить",

        // Recovery gate, enter-existing-passphrase mode (added after update 1.5.1)
        ["common.recovery_enter_intro"] = "У этого профиля ещё нет ключей шифрования. Введите вашу парольную фразу шифрования, чтобы настроить их.",

        // account moderation reconcile (added after update 2.0.0)
        ["common.moderation_warning_for"] = "Предупреждение для {0}",
        ["common.moderation_message_for"] = "Сообщение для {0}",
        ["common.account_disabled_title"] = "Аккаунт заблокирован",
        ["common.account_disabled_body"] = "Эта функция недоступна, пока ваш аккаунт заблокирован.",

        // added after update 2.0.1
        ["common.passphrase_correct_unrecoverable"] = "Кодовая фраза верна, но открыть ею сохранённые ключи не получилось. Пожалуйста, свяжитесь с поддержкой, прежде чем сбрасывать фразу: после сброса старые сообщения останутся нечитаемыми навсегда.",

        // added after update 2.1.3
        ["common.staff_notice_heading_one"] = "У вас есть уведомление от команды",
        ["common.staff_notice_heading_many"] = "У вас есть уведомления от команды: {0}",
        ["common.staff_notice_body"] = "Команда AetherOS прислала вам вот что по вашему аккаунту:",
        ["common.staff_notice_ack"] = "Понятно",
    };
}
