using System.Collections.Generic;

namespace AetherOS.Apps.Doom.Localization;

/// <summary>The Doom cabinet's own UI strings, merged into the central tables at app registration.</summary>
public static class AppStrings
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        // added after update 2.2.3 (doom)
        ["os.doom_intro_question"] = "Will it run doom?",
        ["os.doom_intro_button"] = "no of course not",
        ["os.doom_subtitle"] = "Knee-Deep in the Dead",
        ["os.doom_play"] = "Play",
        ["os.doom_controls"] = "WASD moves, drag the view to turn, Space fires, Shift opens doors, 1-7 pick a weapon, Esc opens the menu.",
        ["os.doom_footer"] = "of course it runs doom...",
        ["os.doom_kills"] = "Kills {0}",
        ["os.doom_sound_on"] = "Sound on",
        ["os.doom_sound_off"] = "Sound off",
        ["os.doom_paused"] = "Paused",
        ["os.doom_resume"] = "Resume",
        ["os.doom_quit"] = "Leave the cabinet",
        ["os.doom_back"] = "Back",
        ["os.doom_missing_title"] = "No cartridge",
        ["os.doom_missing_wad"] = "The cabinet cannot find its game data. Reinstalling the plugin should put it back.",
        ["os.doom_missing_engine"] = "The cabinet would not start. Check the plugin log for what went wrong.",
    };

    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>
    {
        // added after update 2.2.3 (doom)
        ["os.doom_intro_question"] = "Läuft doom darauf?",
        ["os.doom_intro_button"] = "nein natürlich nicht",
        ["os.doom_subtitle"] = "Knietief im Totenreich",
        ["os.doom_play"] = "Spielen",
        ["os.doom_controls"] = "WASD bewegt, ziehen dreht die Sicht, Leertaste schießt, Umschalt öffnet Türen, 1-7 wählt die Waffe, Esc öffnet das Menü.",
        ["os.doom_footer"] = "natürlich läuft doom darauf...",
        ["os.doom_kills"] = "Kills {0}",
        ["os.doom_sound_on"] = "Ton an",
        ["os.doom_sound_off"] = "Ton aus",
        ["os.doom_paused"] = "Pause",
        ["os.doom_resume"] = "Weiter",
        ["os.doom_quit"] = "Automat verlassen",
        ["os.doom_back"] = "Zurück",
        ["os.doom_missing_title"] = "Kein Modul",
        ["os.doom_missing_wad"] = "Der Automat findet seine Spieldaten nicht. Eine Neuinstallation des Plugins bringt sie zurück.",
        ["os.doom_missing_engine"] = "Der Automat ist nicht angesprungen. Im Plugin-Log steht, woran es lag.",
    };

    private static readonly IReadOnlyDictionary<string, string> Es = new Dictionary<string, string>
    {
        // added after update 2.2.3 (doom)
        ["os.doom_intro_question"] = "¿Moverá doom?",
        ["os.doom_intro_button"] = "no claro que no",
        ["os.doom_subtitle"] = "Hasta las rodillas entre muertos",
        ["os.doom_play"] = "Jugar",
        ["os.doom_controls"] = "WASD te mueve, arrastra la vista para girar, Espacio dispara, Mayús abre puertas, 1-7 cambia de arma, Esc abre el menú.",
        ["os.doom_footer"] = "claro que mueve doom...",
        ["os.doom_kills"] = "Bajas {0}",
        ["os.doom_sound_on"] = "Sonido activado",
        ["os.doom_sound_off"] = "Sonido desactivado",
        ["os.doom_paused"] = "En pausa",
        ["os.doom_resume"] = "Continuar",
        ["os.doom_quit"] = "Salir de la máquina",
        ["os.doom_back"] = "Volver",
        ["os.doom_missing_title"] = "Sin cartucho",
        ["os.doom_missing_wad"] = "La máquina no encuentra los datos del juego. Reinstalar el plugin debería devolverlos.",
        ["os.doom_missing_engine"] = "La máquina no arrancó. Mira el registro del plugin para ver qué falló.",
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>
    {
        // added after update 2.2.3 (doom)
        ["os.doom_intro_question"] = "Ça fera tourner doom ?",
        ["os.doom_intro_button"] = "non bien sûr que non",
        ["os.doom_subtitle"] = "Jusqu'aux genoux dans les morts",
        ["os.doom_play"] = "Jouer",
        ["os.doom_controls"] = "ZQSD pour bouger, glissez la vue pour tourner, Espace pour tirer, Maj pour ouvrir, 1-7 pour l'arme, Échap pour le menu.",
        ["os.doom_footer"] = "bien sûr que ça fait tourner doom...",
        ["os.doom_kills"] = "Victimes {0}",
        ["os.doom_sound_on"] = "Son activé",
        ["os.doom_sound_off"] = "Son coupé",
        ["os.doom_paused"] = "En pause",
        ["os.doom_resume"] = "Reprendre",
        ["os.doom_quit"] = "Quitter la borne",
        ["os.doom_back"] = "Retour",
        ["os.doom_missing_title"] = "Pas de cartouche",
        ["os.doom_missing_wad"] = "La borne ne trouve pas ses données de jeu. Réinstaller le plugin devrait les remettre.",
        ["os.doom_missing_engine"] = "La borne n'a pas démarré. Le journal du plugin indique pourquoi.",
    };

    private static readonly IReadOnlyDictionary<string, string> Pt = new Dictionary<string, string>
    {
        // added after update 2.2.3 (doom)
        ["os.doom_intro_question"] = "Vai rodar doom?",
        ["os.doom_intro_button"] = "não claro que não",
        ["os.doom_subtitle"] = "Até aos joelhos entre os mortos",
        ["os.doom_play"] = "Jogar",
        ["os.doom_controls"] = "WASD move, arrasta a vista para virar, Espaço dispara, Shift abre portas, 1-7 troca de arma, Esc abre o menu.",
        ["os.doom_footer"] = "claro que roda doom...",
        ["os.doom_kills"] = "Abates {0}",
        ["os.doom_sound_on"] = "Som ligado",
        ["os.doom_sound_off"] = "Som desligado",
        ["os.doom_paused"] = "Em pausa",
        ["os.doom_resume"] = "Continuar",
        ["os.doom_quit"] = "Sair da máquina",
        ["os.doom_back"] = "Voltar",
        ["os.doom_missing_title"] = "Sem cartucho",
        ["os.doom_missing_wad"] = "A máquina não encontra os dados do jogo. Reinstalar o plugin deve trazê-los de volta.",
        ["os.doom_missing_engine"] = "A máquina não arrancou. Vê o registo do plugin para saber o que falhou.",
    };

    private static readonly IReadOnlyDictionary<string, string> Ru = new Dictionary<string, string>
    {
        // added after update 2.2.3 (doom)
        ["os.doom_intro_question"] = "Тут пойдёт doom?",
        ["os.doom_intro_button"] = "нет конечно нет",
        ["os.doom_subtitle"] = "По колено в мертвецах",
        ["os.doom_play"] = "Играть",
        ["os.doom_controls"] = "WASD — движение, тяни экран, чтобы повернуться, пробел — огонь, Shift открывает двери, 1-7 — оружие, Esc — меню.",
        ["os.doom_footer"] = "конечно, doom тут идёт...",
        ["os.doom_kills"] = "Убийств {0}",
        ["os.doom_sound_on"] = "Звук включён",
        ["os.doom_sound_off"] = "Звук выключен",
        ["os.doom_paused"] = "Пауза",
        ["os.doom_resume"] = "Продолжить",
        ["os.doom_quit"] = "Отойти от автомата",
        ["os.doom_back"] = "Назад",
        ["os.doom_missing_title"] = "Картриджа нет",
        ["os.doom_missing_wad"] = "Автомат не нашёл игровые данные. Переустановка плагина должна их вернуть.",
        ["os.doom_missing_engine"] = "Автомат не завёлся. Загляни в лог плагина, там будет причина.",
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Packs =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en"] = En,
            ["de"] = De,
            ["es"] = Es,
            ["fr"] = Fr,
            ["pt"] = Pt,
            ["ru"] = Ru,
        };
}
