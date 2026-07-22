using System.Collections.Generic;

namespace AetherOS.Apps.Weather;

internal static class AppStrings
{
    public static readonly Dictionary<string, string> En = new()
    {
        ["os.weather_not_ingame"] = "No zone data. Log in to a character to see the forecast.",
        ["os.weather_eorzea_time"] = "Eorzea time",
        ["os.weather_forecast"] = "Forecast",
        ["os.weather_now"] = "Now",
        ["os.weather_control"] = "Weather control",
        ["os.weather_combat"] = "Unavailable during combat.",
        ["os.weather_reset"] = "Restore natural weather",
        ["os.weather_time"] = "Time control",
        ["os.weather_sunrise"] = "Sunrise",
        ["os.weather_noon"] = "Noon",
        ["os.weather_sunset"] = "Sunset",
        ["os.weather_midnight"] = "Midnight",
        ["os.weather_time_reset"] = "Restore natural time",
    };

    public static readonly Dictionary<string, string> De = new()
    {
        ["os.weather_not_ingame"] = "Keine Zonendaten. Logge dich mit einem Charakter ein, um die Vorhersage zu sehen.",
        ["os.weather_eorzea_time"] = "Eorzea-Zeit",
        ["os.weather_forecast"] = "Vorhersage",
        ["os.weather_now"] = "Jetzt",
        ["os.weather_control"] = "Wettersteuerung",
        ["os.weather_combat"] = "Im Kampf nicht verfügbar.",
        ["os.weather_reset"] = "Natürliches Wetter wiederherstellen",
        ["os.weather_time"] = "Zeitsteuerung",
        ["os.weather_sunrise"] = "Sonnenaufgang",
        ["os.weather_noon"] = "Mittag",
        ["os.weather_sunset"] = "Sonnenuntergang",
        ["os.weather_midnight"] = "Mitternacht",
        ["os.weather_time_reset"] = "Natürliche Zeit wiederherstellen",
    };

    public static readonly Dictionary<string, string> Es = new()
    {
        ["os.weather_not_ingame"] = "Sin datos de zona. Inicia sesión con un personaje para ver el pronóstico.",
        ["os.weather_eorzea_time"] = "Hora de Eorzea",
        ["os.weather_forecast"] = "Pronóstico",
        ["os.weather_now"] = "Ahora",
        ["os.weather_control"] = "Control del clima",
        ["os.weather_combat"] = "No disponible durante el combate.",
        ["os.weather_reset"] = "Restaurar clima natural",
        ["os.weather_time"] = "Control de la hora",
        ["os.weather_sunrise"] = "Amanecer",
        ["os.weather_noon"] = "Mediodía",
        ["os.weather_sunset"] = "Atardecer",
        ["os.weather_midnight"] = "Medianoche",
        ["os.weather_time_reset"] = "Restaurar hora natural",
    };

    public static readonly Dictionary<string, string> Fr = new()
    {
        ["os.weather_not_ingame"] = "Aucune donnée de zone. Connectez-vous avec un personnage pour voir les prévisions.",
        ["os.weather_eorzea_time"] = "Heure d'Éorzéa",
        ["os.weather_forecast"] = "Prévisions",
        ["os.weather_now"] = "Maintenant",
        ["os.weather_control"] = "Contrôle de la météo",
        ["os.weather_combat"] = "Indisponible en combat.",
        ["os.weather_reset"] = "Rétablir la météo naturelle",
        ["os.weather_time"] = "Contrôle de l'heure",
        ["os.weather_sunrise"] = "Lever du soleil",
        ["os.weather_noon"] = "Midi",
        ["os.weather_sunset"] = "Coucher du soleil",
        ["os.weather_midnight"] = "Minuit",
        ["os.weather_time_reset"] = "Rétablir l'heure naturelle",
    };

    public static readonly Dictionary<string, string> Pt = new()
    {
        ["os.weather_not_ingame"] = "Sem dados de zona. Entre com um personagem para ver a previsão.",
        ["os.weather_eorzea_time"] = "Hora de Eorzea",
        ["os.weather_forecast"] = "Previsão",
        ["os.weather_now"] = "Agora",
        ["os.weather_control"] = "Controle do clima",
        ["os.weather_combat"] = "Indisponível durante o combate.",
        ["os.weather_reset"] = "Restaurar clima natural",
        ["os.weather_time"] = "Controle da hora",
        ["os.weather_sunrise"] = "Amanhecer",
        ["os.weather_noon"] = "Meio-dia",
        ["os.weather_sunset"] = "Pôr do sol",
        ["os.weather_midnight"] = "Meia-noite",
        ["os.weather_time_reset"] = "Restaurar hora natural",
    };

    public static readonly Dictionary<string, string> Ru = new()
    {
        ["os.weather_not_ingame"] = "Нет данных о зоне. Войдите в игру персонажем, чтобы увидеть прогноз.",
        ["os.weather_eorzea_time"] = "Время Эорзеи",
        ["os.weather_forecast"] = "Прогноз",
        ["os.weather_now"] = "Сейчас",
        ["os.weather_control"] = "Управление погодой",
        ["os.weather_combat"] = "Недоступно во время боя.",
        ["os.weather_reset"] = "Вернуть естественную погоду",
        ["os.weather_time"] = "Управление временем",
        ["os.weather_sunrise"] = "Рассвет",
        ["os.weather_noon"] = "Полдень",
        ["os.weather_sunset"] = "Закат",
        ["os.weather_midnight"] = "Полночь",
        ["os.weather_time_reset"] = "Вернуть естественное время",
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Packs = new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["en"] = En,
        ["de"] = De,
        ["es"] = Es,
        ["fr"] = Fr,
        ["pt"] = Pt,
        ["ru"] = Ru,
    };
}
