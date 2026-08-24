using System.Collections.Generic;

namespace AetherOS.Apps.Breaker.Localization;

/// <summary>The Breaker app's own UI strings, merged into the central tables at app registration.</summary>
public static class AppStrings
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        // added after update 2.1.3 (breaker)
        ["os.ark_subtitle"] = "Break every brick. Catch the capsules.",
        ["os.ark_play"] = "Play",
        ["os.ark_high_scores"] = "My high scores",
        ["os.ark_best"] = "Best {0}",
        ["os.ark_score"] = "Score {0}",
        ["os.ark_level"] = "Level {0}",
        ["os.ark_reached"] = "Reached level {0}",
        ["os.ark_launch_hint"] = "Tap or press space to launch",
        ["os.ark_game_over"] = "Game over",
        ["os.ark_new_record"] = "New record!",
        ["os.ark_play_again"] = "Play again",
        ["os.ark_menu"] = "Menu",
        ["os.ark_no_scores"] = "No scores yet. Go get one!",
        ["os.ark_paused"] = "Paused",
        ["os.ark_resume"] = "Resume",
        ["os.ark_end_run"] = "End run",
    };

    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>
    {
        ["os.ark_subtitle"] = "Zerschlag jeden Stein. Fang die Kapseln.",
        ["os.ark_play"] = "Spielen",
        ["os.ark_high_scores"] = "Meine Bestwerte",
        ["os.ark_best"] = "Beste {0}",
        ["os.ark_score"] = "Punkte {0}",
        ["os.ark_level"] = "Level {0}",
        ["os.ark_reached"] = "Level {0} erreicht",
        ["os.ark_launch_hint"] = "Tippen oder Leertaste zum Start",
        ["os.ark_game_over"] = "Vorbei",
        ["os.ark_new_record"] = "Neuer Rekord!",
        ["os.ark_play_again"] = "Nochmal",
        ["os.ark_menu"] = "Menü",
        ["os.ark_no_scores"] = "Noch keine Punkte. Auf geht's!",
        ["os.ark_paused"] = "Pause",
        ["os.ark_resume"] = "Weiter",
        ["os.ark_end_run"] = "Lauf beenden",
    };

    private static readonly IReadOnlyDictionary<string, string> Es = new Dictionary<string, string>
    {
        ["os.ark_subtitle"] = "Rompe cada ladrillo. Atrapa las cápsulas.",
        ["os.ark_play"] = "Jugar",
        ["os.ark_high_scores"] = "Mis récords",
        ["os.ark_best"] = "Mejor {0}",
        ["os.ark_score"] = "Puntos {0}",
        ["os.ark_level"] = "Nivel {0}",
        ["os.ark_reached"] = "Llegaste al nivel {0}",
        ["os.ark_launch_hint"] = "Toca o pulsa espacio para lanzar",
        ["os.ark_game_over"] = "Fin de la partida",
        ["os.ark_new_record"] = "¡Nuevo récord!",
        ["os.ark_play_again"] = "Otra vez",
        ["os.ark_menu"] = "Menú",
        ["os.ark_no_scores"] = "Aún no hay puntuaciones. ¡A por ellas!",
        ["os.ark_paused"] = "En pausa",
        ["os.ark_resume"] = "Continuar",
        ["os.ark_end_run"] = "Terminar la partida",
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>
    {
        ["os.ark_subtitle"] = "Cassez chaque brique. Attrapez les capsules.",
        ["os.ark_play"] = "Jouer",
        ["os.ark_high_scores"] = "Mes records",
        ["os.ark_best"] = "Record {0}",
        ["os.ark_score"] = "Score {0}",
        ["os.ark_level"] = "Niveau {0}",
        ["os.ark_reached"] = "Niveau {0} atteint",
        ["os.ark_launch_hint"] = "Touchez ou espace pour lancer",
        ["os.ark_game_over"] = "Partie terminée",
        ["os.ark_new_record"] = "Nouveau record !",
        ["os.ark_play_again"] = "Rejouer",
        ["os.ark_menu"] = "Menu",
        ["os.ark_no_scores"] = "Pas encore de score. À vous de jouer !",
        ["os.ark_paused"] = "En pause",
        ["os.ark_resume"] = "Reprendre",
        ["os.ark_end_run"] = "Terminer la partie",
    };

    private static readonly IReadOnlyDictionary<string, string> Pt = new Dictionary<string, string>
    {
        ["os.ark_subtitle"] = "Quebre cada tijolo. Pegue as cápsulas.",
        ["os.ark_play"] = "Jogar",
        ["os.ark_high_scores"] = "Meus recordes",
        ["os.ark_best"] = "Recorde {0}",
        ["os.ark_score"] = "Pontos {0}",
        ["os.ark_level"] = "Nível {0}",
        ["os.ark_reached"] = "Chegou ao nível {0}",
        ["os.ark_launch_hint"] = "Toque ou espaço para lançar",
        ["os.ark_game_over"] = "Fim de jogo",
        ["os.ark_new_record"] = "Novo recorde!",
        ["os.ark_play_again"] = "Jogar de novo",
        ["os.ark_menu"] = "Menu",
        ["os.ark_no_scores"] = "Ainda sem pontuações. Vai lá!",
        ["os.ark_paused"] = "Pausado",
        ["os.ark_resume"] = "Continuar",
        ["os.ark_end_run"] = "Encerrar a partida",
    };

    private static readonly IReadOnlyDictionary<string, string> Ru = new Dictionary<string, string>
    {
        ["os.ark_subtitle"] = "Разбей все блоки. Лови капсулы.",
        ["os.ark_play"] = "Играть",
        ["os.ark_high_scores"] = "Мои рекорды",
        ["os.ark_best"] = "Рекорд {0}",
        ["os.ark_score"] = "Очки {0}",
        ["os.ark_level"] = "Уровень {0}",
        ["os.ark_reached"] = "Пройден уровень {0}",
        ["os.ark_launch_hint"] = "Нажмите или пробел для запуска",
        ["os.ark_game_over"] = "Игра окончена",
        ["os.ark_new_record"] = "Новый рекорд!",
        ["os.ark_play_again"] = "Ещё раз",
        ["os.ark_menu"] = "Меню",
        ["os.ark_no_scores"] = "Рекордов пока нет. Самое время!",
        ["os.ark_paused"] = "Пауза",
        ["os.ark_resume"] = "Продолжить",
        ["os.ark_end_run"] = "Завершить игру",
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
