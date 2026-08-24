using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Services.Translation;

/// <summary>One selectable target language: the code the endpoint takes, the name in the language
/// itself (what the dropdown shows, so everyone finds their own tongue regardless of UI language), the
/// English name (searchable alongside the native one), and <paramref name="Probe"/>: the distinctive
/// letters of the language's orthography that its NAME does not exercise, so the font filter judges the
/// alphabet the translated OUTPUT will need rather than just the label (Turkish reads fine as
/// "Türkçe" while its ş/ğ/ı are missing).</summary>
public sealed record TranslationLanguage(string Code, string NativeName, string EnglishName, string Probe = "")
{
    public bool Matches(string filter) =>
        NativeName.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || EnglishName.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Code.Equals(filter, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The Google Translate target list, native-name ordered by English name. Append-friendly:
/// an unknown stored code simply renders as its code until the list learns it.</summary>
public static class TranslationLanguages
{
    public static TranslationLanguage? Find(string code) =>
        All.Find(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public static string DisplayName(string code) => Find(code)?.NativeName ?? code;

    private static List<TranslationLanguage>? _renderable;

    /// <summary>The languages whose native names the CURRENT font atlas can actually draw; the rest would
    /// render as tofu in the picker, and their translated output would be just as unreadable in a bubble,
    /// so they are not offered at all. Evaluated once per session on the draw thread, against whatever
    /// glyph ranges this install's fonts happen to carry.</summary>
    public static IReadOnlyList<TranslationLanguage> Renderable
    {
        get
        {
            if (_renderable is not null)
            {
                return _renderable;
            }
            var font = ImGui.GetFont();
            var list = new List<TranslationLanguage>(All.Count);
            foreach (var language in All)
            {
                if (CanRender(font, language))
                {
                    list.Add(language);
                }
            }
            _renderable = list;
            return list;
        }
    }

    private static bool CanRender(ImFontPtr font, TranslationLanguage language) =>
        CanRender(font, language.NativeName) && CanRender(font, language.Probe);

    private static bool CanRender(ImFontPtr font, string text)
    {
        foreach (var c in text)
        {
            if (c == ' ' || char.IsControl(c))
            {
                continue;
            }
            unsafe
            {
                if (font.FindGlyphNoFallback(c) == null)
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>The sensible default target for a given plugin UI language name ("English", "French").</summary>
    public static string DefaultForPluginLanguage(string pluginLanguage) => pluginLanguage switch
    {
        "German" => "de",
        "Spanish" => "es",
        "French" => "fr",
        "Portuguese" => "pt",
        "Russian" => "ru",
        "Japanese" => "ja",
        _ => "en",
    };

    public static readonly List<TranslationLanguage> All =
    [
        new("af", "Afrikaans", "Afrikaans", "êëïôû"),
        new("sq", "Shqip", "Albanian", "çë"),
        new("am", "አማርኛ", "Amharic"),
        new("ar", "العربية", "Arabic"),
        new("hy", "Հայերեն", "Armenian"),
        new("az", "Azərbaycan dili", "Azerbaijani", "əğıöüçş"),
        new("eu", "Euskara", "Basque", "ñ"),
        new("be", "Беларуская", "Belarusian", "ўі"),
        new("bn", "বাংলা", "Bengali"),
        new("bs", "Bosanski", "Bosnian", "čćđšž"),
        new("bg", "Български", "Bulgarian"),
        new("ca", "Català", "Catalan", "àçéèíïóòúü"),
        new("ceb", "Cebuano", "Cebuano"),
        new("zh-CN", "简体中文", "Chinese (Simplified)"),
        new("zh-TW", "繁體中文", "Chinese (Traditional)"),
        new("co", "Corsu", "Corsican", "àèìòù"),
        new("hr", "Hrvatski", "Croatian", "čćđšž"),
        new("cs", "Čeština", "Czech", "áčďéěíňóřšťúůýž"),
        new("da", "Dansk", "Danish", "æøå"),
        new("nl", "Nederlands", "Dutch", "éëï"),
        new("en", "English", "English"),
        new("eo", "Esperanto", "Esperanto", "ĉĝĥĵŝŭ"),
        new("et", "Eesti", "Estonian", "äöüõšž"),
        new("fi", "Suomi", "Finnish", "äöå"),
        new("fr", "Français", "French", "éèêëàâçîïôùûœ"),
        new("fy", "Frysk", "Frisian", "âêôûú"),
        new("gl", "Galego", "Galician", "áéíóúñ"),
        new("ka", "ქართული", "Georgian"),
        new("de", "Deutsch", "German", "äöüß"),
        new("el", "Ελληνικά", "Greek"),
        new("gu", "ગુજરાતી", "Gujarati"),
        new("ht", "Kreyòl ayisyen", "Haitian Creole", "èò"),
        new("ha", "Hausa", "Hausa", "ɓɗƙ"),
        new("haw", "ʻŌlelo Hawaiʻi", "Hawaiian", "āēīōūʻ"),
        new("iw", "עברית", "Hebrew"),
        new("hi", "हिन्दी", "Hindi"),
        new("hmn", "Hmoob", "Hmong"),
        new("hu", "Magyar", "Hungarian", "áéíóöőúüű"),
        new("is", "Íslenska", "Icelandic", "áðéíóúýþæö"),
        new("ig", "Igbo", "Igbo", "ịọụṅ"),
        new("id", "Bahasa Indonesia", "Indonesian"),
        new("ga", "Gaeilge", "Irish", "áéíóú"),
        new("it", "Italiano", "Italian", "àèéìòù"),
        new("ja", "日本語", "Japanese"),
        new("jv", "Basa Jawa", "Javanese", "é"),
        new("kn", "ಕನ್ನಡ", "Kannada"),
        new("kk", "Қазақ тілі", "Kazakh", "әғқңөұүһі"),
        new("km", "ខ្មែរ", "Khmer"),
        new("rw", "Kinyarwanda", "Kinyarwanda"),
        new("ko", "한국어", "Korean"),
        new("ku", "Kurdî", "Kurdish", "çêîşû"),
        new("ky", "Кыргызча", "Kyrgyz", "ңөү"),
        new("lo", "ລາວ", "Lao"),
        new("la", "Latina", "Latin", "āēīōū"),
        new("lv", "Latviešu", "Latvian", "āčēģīķļņšūž"),
        new("lt", "Lietuvių", "Lithuanian", "ąčęėįšųūž"),
        new("lb", "Lëtzebuergesch", "Luxembourgish", "äéëêè"),
        new("mk", "Македонски", "Macedonian", "ѓѕјљњќџ"),
        new("mg", "Malagasy", "Malagasy", "ô"),
        new("ms", "Bahasa Melayu", "Malay"),
        new("ml", "മലയാളം", "Malayalam"),
        new("mt", "Malti", "Maltese", "ċġħż"),
        new("mi", "Te Reo Māori", "Maori", "āēīōū"),
        new("mr", "मराठी", "Marathi"),
        new("mn", "Монгол", "Mongolian", "өү"),
        new("my", "မြန်မာစာ", "Myanmar (Burmese)"),
        new("ne", "नेपाली", "Nepali"),
        new("no", "Norsk", "Norwegian", "æøå"),
        new("ny", "Chichewa", "Nyanja (Chichewa)", "ŵ"),
        new("or", "ଓଡ଼ିଆ", "Odia (Oriya)"),
        new("ps", "پښتو", "Pashto"),
        new("fa", "فارسی", "Persian"),
        new("pl", "Polski", "Polish", "ąćęłńóśźż"),
        new("pt", "Português", "Portuguese", "ãõáâàçéêíóôú"),
        new("pa", "ਪੰਜਾਬੀ", "Punjabi"),
        new("ro", "Română", "Romanian", "ăâîșț"),
        new("ru", "Русский", "Russian"),
        new("sm", "Gagana Samoa", "Samoan", "āēīōū"),
        new("gd", "Gàidhlig", "Scots Gaelic", "àèìòù"),
        new("sr", "Српски", "Serbian", "ђћџљњј"),
        new("st", "Sesotho", "Sesotho"),
        new("sn", "ChiShona", "Shona"),
        new("sd", "سنڌي", "Sindhi"),
        new("si", "සිංහල", "Sinhala"),
        new("sk", "Slovenčina", "Slovak", "áäčďéíĺľňóôŕšťúýž"),
        new("sl", "Slovenščina", "Slovenian", "čšž"),
        new("so", "Soomaali", "Somali"),
        new("es", "Español", "Spanish", "áéíóúüñ¿¡"),
        new("su", "Basa Sunda", "Sundanese", "é"),
        new("sw", "Kiswahili", "Swahili"),
        new("sv", "Svenska", "Swedish", "åäö"),
        new("tl", "Filipino", "Tagalog (Filipino)", "ñ"),
        new("tg", "Тоҷикӣ", "Tajik", "ҷҳқғӯӣ"),
        new("ta", "தமிழ்", "Tamil"),
        new("tt", "Татарча", "Tatar", "әөүҗңһ"),
        new("te", "తెలుగు", "Telugu"),
        new("th", "ไทย", "Thai"),
        new("tr", "Türkçe", "Turkish", "çğıİöşü"),
        new("tk", "Türkmençe", "Turkmen", "äçňöşüýž"),
        new("uk", "Українська", "Ukrainian", "єіїґ"),
        new("ur", "اردو", "Urdu"),
        new("ug", "ئۇيغۇرچە", "Uyghur"),
        new("uz", "O'zbekcha", "Uzbek", "ʻ"),
        new("vi", "Tiếng Việt", "Vietnamese", "ăâđêôơưạềễịọủứỹ"),
        new("cy", "Cymraeg", "Welsh", "ŵŷâêîôû"),
        new("xh", "isiXhosa", "Xhosa"),
        new("yi", "ייִדיש", "Yiddish"),
        new("yo", "Yorùbá", "Yoruba", "ẹọṣáà"),
        new("zu", "isiZulu", "Zulu"),
    ];
}
