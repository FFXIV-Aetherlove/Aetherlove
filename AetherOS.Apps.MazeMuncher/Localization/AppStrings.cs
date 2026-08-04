using System.Collections.Generic;

namespace AetherOS.Apps.MazeMuncher.Localization;

/// <summary>The Maze Muncher app's own UI strings, merged into the central tables at app registration.</summary>
public static class AppStrings
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        // added after update 2.1.3 (maze muncher)
        ["os.muncher_subtitle"] = "Eat the dots. Mind the ghosts.",
        ["os.muncher_play"] = "Play",
        ["os.muncher_high_scores"] = "High scores",
        ["os.muncher_best"] = "Best {0}",
        ["os.muncher_score"] = "Score {0}",
        ["os.muncher_level"] = "Level {0}",
        ["os.muncher_ready"] = "READY!",
        ["os.muncher_reached"] = "Reached level {0}",
        ["os.muncher_game_over"] = "Game over",
        ["os.muncher_new_record"] = "New record!",
        ["os.muncher_play_again"] = "Play again",
        ["os.muncher_menu"] = "Menu",
        ["os.muncher_no_scores"] = "No scores yet. Go get one!",
        ["os.muncher_paused"] = "Paused",
        ["os.muncher_resume"] = "Resume",
    };

    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>
    {
        ["os.muncher_subtitle"] = "Punkte fressen. Geister meiden.",
        ["os.muncher_play"] = "Spielen",
        ["os.muncher_high_scores"] = "Bestenliste",
        ["os.muncher_best"] = "Beste {0}",
        ["os.muncher_score"] = "Punkte {0}",
        ["os.muncher_level"] = "Level {0}",
        ["os.muncher_ready"] = "BEREIT!",
        ["os.muncher_reached"] = "Level {0} erreicht",
        ["os.muncher_game_over"] = "Vorbei",
        ["os.muncher_new_record"] = "Neuer Rekord!",
        ["os.muncher_play_again"] = "Nochmal",
        ["os.muncher_menu"] = "Menü",
        ["os.muncher_no_scores"] = "Noch keine Punkte. Auf geht's!",
        ["os.muncher_paused"] = "Pause",
        ["os.muncher_resume"] = "Weiter",
    };

    private static readonly IReadOnlyDictionary<string, string> Es = new Dictionary<string, string>
    {
        ["os.muncher_subtitle"] = "Come los puntos. Cuidado con los fantasmas.",
        ["os.muncher_play"] = "Jugar",
        ["os.muncher_high_scores"] = "Mejores puntuaciones",
        ["os.muncher_best"] = "Mejor {0}",
        ["os.muncher_score"] = "Puntos {0}",
        ["os.muncher_level"] = "Nivel {0}",
        ["os.muncher_ready"] = "¡LISTO!",
        ["os.muncher_reached"] = "Llegaste al nivel {0}",
        ["os.muncher_game_over"] = "Fin de la partida",
        ["os.muncher_new_record"] = "¡Nuevo récord!",
        ["os.muncher_play_again"] = "Otra vez",
        ["os.muncher_menu"] = "Menú",
        ["os.muncher_no_scores"] = "Aún no hay puntuaciones. ¡A por ellas!",
        ["os.muncher_paused"] = "En pausa",
        ["os.muncher_resume"] = "Continuar",
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>
    {
        ["os.muncher_subtitle"] = "Mangez les points. Méfiez-vous des fantômes.",
        ["os.muncher_play"] = "Jouer",
        ["os.muncher_high_scores"] = "Meilleurs scores",
        ["os.muncher_best"] = "Record {0}",
        ["os.muncher_score"] = "Score {0}",
        ["os.muncher_level"] = "Niveau {0}",
        ["os.muncher_ready"] = "PRÊT !",
        ["os.muncher_reached"] = "Niveau {0} atteint",
        ["os.muncher_game_over"] = "Partie terminée",
        ["os.muncher_new_record"] = "Nouveau record !",
        ["os.muncher_play_again"] = "Rejouer",
        ["os.muncher_menu"] = "Menu",
        ["os.muncher_no_scores"] = "Pas encore de score. À vous de jouer !",
        ["os.muncher_paused"] = "En pause",
        ["os.muncher_resume"] = "Reprendre",
    };

    private static readonly IReadOnlyDictionary<string, string> Pt = new Dictionary<string, string>
    {
        ["os.muncher_subtitle"] = "Coma os pontos. Cuidado com os fantasmas.",
        ["os.muncher_play"] = "Jogar",
        ["os.muncher_high_scores"] = "Melhores pontuações",
        ["os.muncher_best"] = "Recorde {0}",
        ["os.muncher_score"] = "Pontos {0}",
        ["os.muncher_level"] = "Nível {0}",
        ["os.muncher_ready"] = "PRONTO!",
        ["os.muncher_reached"] = "Chegou ao nível {0}",
        ["os.muncher_game_over"] = "Fim de jogo",
        ["os.muncher_new_record"] = "Novo recorde!",
        ["os.muncher_play_again"] = "Jogar de novo",
        ["os.muncher_menu"] = "Menu",
        ["os.muncher_no_scores"] = "Ainda sem pontuações. Vai lá!",
        ["os.muncher_paused"] = "Pausado",
        ["os.muncher_resume"] = "Continuar",
    };

    private static readonly IReadOnlyDictionary<string, string> Ru = new Dictionary<string, string>
    {
        ["os.muncher_subtitle"] = "Ешь точки. Берегись призраков.",
        ["os.muncher_play"] = "Играть",
        ["os.muncher_high_scores"] = "Рекорды",
        ["os.muncher_best"] = "Рекорд {0}",
        ["os.muncher_score"] = "Очки {0}",
        ["os.muncher_level"] = "Уровень {0}",
        ["os.muncher_ready"] = "ГОТОВ!",
        ["os.muncher_reached"] = "Дошёл до уровня {0}",
        ["os.muncher_game_over"] = "Игра окончена",
        ["os.muncher_new_record"] = "Новый рекорд!",
        ["os.muncher_play_again"] = "Ещё раз",
        ["os.muncher_menu"] = "Меню",
        ["os.muncher_no_scores"] = "Рекордов пока нет. Самое время!",
        ["os.muncher_paused"] = "Пауза",
        ["os.muncher_resume"] = "Продолжить",
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
