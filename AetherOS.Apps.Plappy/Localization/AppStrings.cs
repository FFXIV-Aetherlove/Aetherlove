using System.Collections.Generic;

namespace AetherOS.Apps.Plappy.Localization;

/// <summary>The Plappy Birb app's own UI strings, merged into the central tables at app registration.</summary>
public static class AppStrings
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        // added after update 2.2.3 (plappy)
        ["os.plappy_subtitle"] = "Flap. Squeeze through. Don't clip a pillar.",
        ["os.plappy_play"] = "Play",
        ["os.plappy_high_scores"] = "High scores",
        ["os.plappy_best"] = "Best {0}",
        ["os.plappy_score"] = "Score {0}",
        ["os.plappy_tier"] = "Tier {0}",
        ["os.plappy_tier_up"] = "Tier {0}!",
        ["os.plappy_pillars"] = "Pillars: {0}",
        ["os.plappy_tap"] = "Tap to flap",
        ["os.plappy_game_over"] = "Game over",
        ["os.plappy_new_record"] = "New record!",
        ["os.plappy_play_again"] = "Play again",
        ["os.plappy_menu"] = "Menu",
        ["os.plappy_no_scores"] = "No scores yet. Go get one!",
        ["os.plappy_paused"] = "Paused",
        ["os.plappy_resume"] = "Resume",
    };

    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>
    {
        ["os.plappy_subtitle"] = "Flattern. Durchschlüpfen. Bloß nicht anecken.",
        ["os.plappy_play"] = "Spielen",
        ["os.plappy_high_scores"] = "Bestenliste",
        ["os.plappy_best"] = "Beste {0}",
        ["os.plappy_score"] = "Punkte {0}",
        ["os.plappy_tier"] = "Stufe {0}",
        ["os.plappy_tier_up"] = "Stufe {0}!",
        ["os.plappy_pillars"] = "Säulen: {0}",
        ["os.plappy_tap"] = "Tippen zum Flattern",
        ["os.plappy_game_over"] = "Vorbei",
        ["os.plappy_new_record"] = "Neuer Rekord!",
        ["os.plappy_play_again"] = "Nochmal",
        ["os.plappy_menu"] = "Menü",
        ["os.plappy_no_scores"] = "Noch keine Punkte. Auf geht's!",
        ["os.plappy_paused"] = "Pause",
        ["os.plappy_resume"] = "Weiter",
    };

    private static readonly IReadOnlyDictionary<string, string> Es = new Dictionary<string, string>
    {
        ["os.plappy_subtitle"] = "Aletea. Cuélate. No roces las columnas.",
        ["os.plappy_play"] = "Jugar",
        ["os.plappy_high_scores"] = "Mejores puntuaciones",
        ["os.plappy_best"] = "Mejor {0}",
        ["os.plappy_score"] = "Puntos {0}",
        ["os.plappy_tier"] = "Nivel {0}",
        ["os.plappy_tier_up"] = "¡Nivel {0}!",
        ["os.plappy_pillars"] = "Columnas: {0}",
        ["os.plappy_tap"] = "Toca para volar",
        ["os.plappy_game_over"] = "Fin de la partida",
        ["os.plappy_new_record"] = "¡Nuevo récord!",
        ["os.plappy_play_again"] = "Otra vez",
        ["os.plappy_menu"] = "Menú",
        ["os.plappy_no_scores"] = "Aún no hay puntuaciones. ¡A por ellas!",
        ["os.plappy_paused"] = "En pausa",
        ["os.plappy_resume"] = "Continuar",
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>
    {
        ["os.plappy_subtitle"] = "Battez des ailes. Faufilez-vous. Évitez les piliers.",
        ["os.plappy_play"] = "Jouer",
        ["os.plappy_high_scores"] = "Meilleurs scores",
        ["os.plappy_best"] = "Record {0}",
        ["os.plappy_score"] = "Score {0}",
        ["os.plappy_tier"] = "Palier {0}",
        ["os.plappy_tier_up"] = "Palier {0} !",
        ["os.plappy_pillars"] = "Piliers : {0}",
        ["os.plappy_tap"] = "Touchez pour voler",
        ["os.plappy_game_over"] = "Partie terminée",
        ["os.plappy_new_record"] = "Nouveau record !",
        ["os.plappy_play_again"] = "Rejouer",
        ["os.plappy_menu"] = "Menu",
        ["os.plappy_no_scores"] = "Pas encore de score. À vous de jouer !",
        ["os.plappy_paused"] = "En pause",
        ["os.plappy_resume"] = "Reprendre",
    };

    private static readonly IReadOnlyDictionary<string, string> Pt = new Dictionary<string, string>
    {
        ["os.plappy_subtitle"] = "Bata as asas. Passe no meio. Não encoste nos pilares.",
        ["os.plappy_play"] = "Jogar",
        ["os.plappy_high_scores"] = "Melhores pontuações",
        ["os.plappy_best"] = "Recorde {0}",
        ["os.plappy_score"] = "Pontos {0}",
        ["os.plappy_tier"] = "Nível {0}",
        ["os.plappy_tier_up"] = "Nível {0}!",
        ["os.plappy_pillars"] = "Pilares: {0}",
        ["os.plappy_tap"] = "Toque para voar",
        ["os.plappy_game_over"] = "Fim de jogo",
        ["os.plappy_new_record"] = "Novo recorde!",
        ["os.plappy_play_again"] = "Jogar de novo",
        ["os.plappy_menu"] = "Menu",
        ["os.plappy_no_scores"] = "Ainda sem pontuações. Vai lá!",
        ["os.plappy_paused"] = "Pausado",
        ["os.plappy_resume"] = "Continuar",
    };

    private static readonly IReadOnlyDictionary<string, string> Ru = new Dictionary<string, string>
    {
        ["os.plappy_subtitle"] = "Маши крыльями. Пролетай в щель. Не задень столбы.",
        ["os.plappy_play"] = "Играть",
        ["os.plappy_high_scores"] = "Рекорды",
        ["os.plappy_best"] = "Рекорд {0}",
        ["os.plappy_score"] = "Очки {0}",
        ["os.plappy_tier"] = "Уровень {0}",
        ["os.plappy_tier_up"] = "Уровень {0}!",
        ["os.plappy_pillars"] = "Столбы: {0}",
        ["os.plappy_tap"] = "Нажми и лети",
        ["os.plappy_game_over"] = "Игра окончена",
        ["os.plappy_new_record"] = "Новый рекорд!",
        ["os.plappy_play_again"] = "Ещё раз",
        ["os.plappy_menu"] = "Меню",
        ["os.plappy_no_scores"] = "Рекордов пока нет. Самое время!",
        ["os.plappy_paused"] = "Пауза",
        ["os.plappy_resume"] = "Продолжить",
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
