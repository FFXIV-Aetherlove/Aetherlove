namespace AetherLove.Services.Localization;

internal static class HubErrorsRu
{
    public static readonly System.Collections.Generic.Dictionary<string, string> Strings = new()
    {
        ["huberror.generic"] = "Произошла непредвиденная ошибка сервера.",
        ["huberror.generic_detail"] = "Произошла ошибка: {0}",
        ["huberror.invalid_request"] = "Сервер отклонил запрос. Если это повторяется, проверьте наличие обновления плагина.",
        ["huberror.unauthenticated"] = "Ваша сессия больше недействительна. Пожалуйста, войдите снова.",
        ["huberror.banned"] = "Ваш аккаунт заблокирован.",
        ["huberror.rate_limited"] = "Вы делаете это слишком часто. Попробуйте снова чуть позже.",
        ["huberror.profile_not_found"] = "Профиль не найден.",
        ["huberror.profile_not_visible"] = "Этот профиль недоступен.",
        ["huberror.deck_expired"] = "Этого профиля больше нет в вашей колоде. Обновите колоду и попробуйте снова.",
        ["huberror.no_active_match"] = "У вас больше нет пары с этим игроком.",
        ["huberror.peer_keys_missing"] = "Этот пользователь ещё не настроил E2E-шифрование и пока не может общаться в чате. Попробуйте позже.",
        ["huberror.key_bundle_exists"] = "Ключи шифрования для этого аккаунта уже настроены.",
        ["huberror.message_too_large"] = "Это сообщение слишком длинное для отправки.",
        ["huberror.bio_too_long"] = "Ваша анкета превышает лимит в {0} символов.",
        ["huberror.lalafell_erp"] = "Ролевые игры для взрослых недоступны для персонажей-лалафелей.",
        ["huberror.lalafell_nsfw"] = "Функции NSFW недоступны для персонажей-лалафелей.",
        ["huberror.lalafell_nsfw_photo"] = "Фотографии NSFW недоступны для персонажей-лалафелей.",
        ["huberror.nsfw_disable_blocked"] = "Удалите фотографии NSFW и отключите ролевые игры 18+, прежде чем отключать NSFW.",
        ["huberror.img_too_large"] = "Изображение слишком большое ({0} МБ). Максимум — {1} МБ.",
        ["huberror.img_dimensions_too_large"] = "Изображение слишком большое ({0}×{1}). Длинная сторона может быть не более {2}px.",
        ["huberror.img_crop_too_small"] = "Область обрезки слишком мала (минимум {0}px с каждой стороны).",
        ["huberror.img_decode_failed"] = "Не удалось прочитать изображение. Поддерживаемые форматы: PNG, JPEG, WebP, GIF.",
        ["huberror.img_payload_invalid"] = "Не удалось загрузить фотографию. Пожалуйста, выберите изображение ещё раз.",
        ["huberror.report_self"] = "Вы не можете пожаловаться на самого себя.",
        ["huberror.report_reason_required"] = "Пожалуйста, опишите проблему.",
        ["huberror.report_reason_too_long"] = "Причина слишком длинная (максимум {0} символов).",
        ["huberror.report_target_gone"] = "Этот профиль больше не существует.",
        ["huberror.report_duplicate"] = "Вы уже недавно жаловались на этого пользователя. Команда модераторов сейчас в процессе ее расмотра.",
        ["huberror.feedback_required"] = "Пожалуйста, введите сообщение перед отправкой.",
        // added after update (1.3.1)
        ["huberror.reswipe_nothing_to_undo"] = "Нечего отменять.",
        ["huberror.reswipe_already_matched"] = "Нельзя отменить свайп по профилю, с которым у вас уже есть пара.",
        ["huberror.reswipe_quota_exhausted"] = "Вы уже использовали отмену на сегодня.",
        ["huberror.superlike_quota_exhausted"] = "Суперлайки на сегодня закончились.",

        // added after update 1.4.3
        ["huberror.character_limit_reached"] = "У вас может быть не более {0} RP-персонажей.",
        ["huberror.character_name_invalid"] = "Имя персонажа должно быть от 3 до 50 символов.",
        ["huberror.character_not_found"] = "Этот персонаж больше не существует. Обновите и попробуйте снова.",

        // added after update 1.5.0
        ["huberror.patreon_disabled"] = "Привязка Patreon сейчас недоступна.",
        ["huberror.patreon_already_linked"] = "К вашему профилю уже привязан аккаунт Patreon.",
        ["huberror.patreon_not_linked"] = "К вашему профилю не привязан аккаунт Patreon.",
        ["huberror.patreon_account_taken"] = "Этот аккаунт Patreon уже привязан к другому аккаунту AetherLove.",
        ["huberror.patreon_link_failed"] = "Не удалось завершить привязку Patreon. Попробуйте ещё раз.",
        ["huberror.places_disabled"] = "Раздел «Места» сейчас недоступен.",
        ["huberror.venue_not_found"] = "Это заведение больше не существует.",
        ["huberror.venue_limit_reached"] = "Вы достигли лимита в {0} заведений.",
        ["huberror.venue_name_invalid"] = "Название заведения должно содержать от 3 до 60 символов.",
        ["huberror.venue_description_too_long"] = "Описание превышает лимит в {0} символов.",
        ["huberror.venue_times_invalid"] = "Одно из времён работы недопустимо.",
        ["huberror.venue_times_too_many"] = "У заведения может быть не более {0} времён работы.",
        ["huberror.venue_review_own"] = "Нельзя оставить отзыв о собственном заведении.",
        ["huberror.venue_review_too_long"] = "Ваш отзыв превышает лимит в {0} символов.",
        ["huberror.venue_review_rating_invalid"] = "Выберите оценку от 1 до 5 звёзд.",
        ["huberror.venue_rsvp_invalid"] = "На это открытие больше нельзя записаться.",
    };
}
