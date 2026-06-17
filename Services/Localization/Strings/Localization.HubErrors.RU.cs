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
        ["huberror.peer_keys_missing"] = "Ваша пара ещё не завершила настройку зашифрованного чата. Попробуйте позже.",
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
    };
}
