using System.Collections.Generic;

namespace AetherOS.Apps.VoidInvaders.Localization;

/// <summary>The Void Invaders app's own UI strings, merged into the central tables at app registration.</summary>
public static class AppStrings
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        // added after update 2.1.3 (void invaders)
        ["os.invaders_subtitle"] = "Slide, shoot, survive.",
        ["os.invaders_play"] = "Play",
        ["os.invaders_high_scores"] = "High scores",
        ["os.invaders_best"] = "Best {0}",
        ["os.invaders_score"] = "Score {0}",
        ["os.invaders_wave"] = "Wave {0}",
        ["os.invaders_reached"] = "Reached wave {0}",
        ["os.invaders_game_over"] = "Game over",
        ["os.invaders_new_record"] = "New record!",
        ["os.invaders_play_again"] = "Play again",
        ["os.invaders_menu"] = "Menu",
        ["os.invaders_no_scores"] = "No scores yet. Go get one!",
        ["os.invaders_paused"] = "Paused",
        ["os.invaders_resume"] = "Resume",
    };

    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>
    {
        ["os.invaders_subtitle"] = "Ausweichen, schießen, überleben.",
        ["os.invaders_play"] = "Spielen",
        ["os.invaders_high_scores"] = "Bestenliste",
        ["os.invaders_best"] = "Beste {0}",
        ["os.invaders_score"] = "Punkte {0}",
        ["os.invaders_wave"] = "Welle {0}",
        ["os.invaders_reached"] = "Welle {0} erreicht",
        ["os.invaders_game_over"] = "Vorbei",
        ["os.invaders_new_record"] = "Neuer Rekord!",
        ["os.invaders_play_again"] = "Nochmal",
        ["os.invaders_menu"] = "Menü",
        ["os.invaders_no_scores"] = "Noch keine Punkte. Auf geht's!",
        ["os.invaders_paused"] = "Pause",
        ["os.invaders_resume"] = "Weiter",
    };

    private static readonly IReadOnlyDictionary<string, string> Es = new Dictionary<string, string>
    {
        ["os.invaders_subtitle"] = "Muévete, dispara, sobrevive.",
        ["os.invaders_play"] = "Jugar",
        ["os.invaders_high_scores"] = "Mejores puntuaciones",
        ["os.invaders_best"] = "Mejor {0}",
        ["os.invaders_score"] = "Puntos {0}",
        ["os.invaders_wave"] = "Oleada {0}",
        ["os.invaders_reached"] = "Llegaste a la oleada {0}",
        ["os.invaders_game_over"] = "Fin de la partida",
        ["os.invaders_new_record"] = "¡Nuevo récord!",
        ["os.invaders_play_again"] = "Otra vez",
        ["os.invaders_menu"] = "Menú",
        ["os.invaders_no_scores"] = "Aún no hay puntuaciones. ¡A por ellas!",
        ["os.invaders_paused"] = "En pausa",
        ["os.invaders_resume"] = "Continuar",
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>
    {
        ["os.invaders_subtitle"] = "Glissez, tirez, survivez.",
        ["os.invaders_play"] = "Jouer",
        ["os.invaders_high_scores"] = "Meilleurs scores",
        ["os.invaders_best"] = "Record {0}",
        ["os.invaders_score"] = "Score {0}",
        ["os.invaders_wave"] = "Vague {0}",
        ["os.invaders_reached"] = "Vague {0} atteinte",
        ["os.invaders_game_over"] = "Partie terminée",
        ["os.invaders_new_record"] = "Nouveau record !",
        ["os.invaders_play_again"] = "Rejouer",
        ["os.invaders_menu"] = "Menu",
        ["os.invaders_no_scores"] = "Pas encore de score. À vous de jouer !",
        ["os.invaders_paused"] = "En pause",
        ["os.invaders_resume"] = "Reprendre",
    };

    private static readonly IReadOnlyDictionary<string, string> Pt = new Dictionary<string, string>
    {
        ["os.invaders_subtitle"] = "Deslize, atire, sobreviva.",
        ["os.invaders_play"] = "Jogar",
        ["os.invaders_high_scores"] = "Melhores pontuações",
        ["os.invaders_best"] = "Recorde {0}",
        ["os.invaders_score"] = "Pontos {0}",
        ["os.invaders_wave"] = "Onda {0}",
        ["os.invaders_reached"] = "Chegou à onda {0}",
        ["os.invaders_game_over"] = "Fim de jogo",
        ["os.invaders_new_record"] = "Novo recorde!",
        ["os.invaders_play_again"] = "Jogar de novo",
        ["os.invaders_menu"] = "Menu",
        ["os.invaders_no_scores"] = "Ainda sem pontuações. Vai lá!",
        ["os.invaders_paused"] = "Pausado",
        ["os.invaders_resume"] = "Continuar",
    };

    private static readonly IReadOnlyDictionary<string, string> Ru = new Dictionary<string, string>
    {
        ["os.invaders_subtitle"] = "Двигайся, стреляй, выживай.",
        ["os.invaders_play"] = "Играть",
        ["os.invaders_high_scores"] = "Рекорды",
        ["os.invaders_best"] = "Рекорд {0}",
        ["os.invaders_score"] = "Очки {0}",
        ["os.invaders_wave"] = "Волна {0}",
        ["os.invaders_reached"] = "Дошёл до волны {0}",
        ["os.invaders_game_over"] = "Игра окончена",
        ["os.invaders_new_record"] = "Новый рекорд!",
        ["os.invaders_play_again"] = "Ещё раз",
        ["os.invaders_menu"] = "Меню",
        ["os.invaders_no_scores"] = "Рекордов пока нет. Самое время!",
        ["os.invaders_paused"] = "Пауза",
        ["os.invaders_resume"] = "Продолжить",
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
