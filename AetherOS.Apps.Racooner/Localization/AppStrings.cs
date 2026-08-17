using System.Collections.Generic;

namespace AetherOS.Apps.Racooner.Localization;

/// <summary>The Racooner app's own UI strings, merged into the central tables at app registration.</summary>
public static class AppStrings
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        // added after update 2.3.3 (racooner)
        ["os.racooner_subtitle"] = "Hop the traffic, ride the stream, fill all five dens.",
        ["os.racooner_dens"] = "Dens {0}/{1}",
        ["os.racooner_play"] = "Play",
        ["os.racooner_high_scores"] = "High scores",
        ["os.racooner_best"] = "Best {0}",
        ["os.racooner_score"] = "Score {0}",
        ["os.racooner_level"] = "Level {0}",
        ["os.racooner_reached"] = "Reached level {0}",
        ["os.racooner_banked"] = "Dens filled: {0}",
        ["os.racooner_game_over"] = "Game over",
        ["os.racooner_new_record"] = "New record!",
        ["os.racooner_play_again"] = "Play again",
        ["os.racooner_menu"] = "Menu",
        ["os.racooner_no_scores"] = "No scores yet. Go get one!",
        ["os.racooner_paused"] = "Paused",
        ["os.racooner_resume"] = "Resume",
    };

    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>
    {
        // added after update 2.3.3 (racooner)
        ["os.racooner_subtitle"] = "Durch den Verkehr, über den Strom, alle fünf Baue füllen.",
        ["os.racooner_dens"] = "Baue {0}/{1}",
        ["os.racooner_play"] = "Spielen",
        ["os.racooner_high_scores"] = "Bestenliste",
        ["os.racooner_best"] = "Beste {0}",
        ["os.racooner_score"] = "Punkte {0}",
        ["os.racooner_level"] = "Level {0}",
        ["os.racooner_reached"] = "Level {0} erreicht",
        ["os.racooner_banked"] = "Gefüllte Baue: {0}",
        ["os.racooner_game_over"] = "Vorbei",
        ["os.racooner_new_record"] = "Neuer Rekord!",
        ["os.racooner_play_again"] = "Nochmal",
        ["os.racooner_menu"] = "Menü",
        ["os.racooner_no_scores"] = "Noch keine Punkte. Auf geht's!",
        ["os.racooner_paused"] = "Pause",
        ["os.racooner_resume"] = "Weiter",
    };

    private static readonly IReadOnlyDictionary<string, string> Es = new Dictionary<string, string>
    {
        // added after update 2.3.3 (racooner)
        ["os.racooner_subtitle"] = "Esquiva el tráfico, cruza la corriente y llena las cinco madrigueras.",
        ["os.racooner_dens"] = "Madrigueras {0}/{1}",
        ["os.racooner_play"] = "Jugar",
        ["os.racooner_high_scores"] = "Mejores puntuaciones",
        ["os.racooner_best"] = "Mejor {0}",
        ["os.racooner_score"] = "Puntos {0}",
        ["os.racooner_level"] = "Nivel {0}",
        ["os.racooner_reached"] = "Llegaste al nivel {0}",
        ["os.racooner_banked"] = "Madrigueras llenas: {0}",
        ["os.racooner_game_over"] = "Fin de la partida",
        ["os.racooner_new_record"] = "¡Nuevo récord!",
        ["os.racooner_play_again"] = "Otra vez",
        ["os.racooner_menu"] = "Menú",
        ["os.racooner_no_scores"] = "Aún no hay puntuaciones. ¡A por ellas!",
        ["os.racooner_paused"] = "En pausa",
        ["os.racooner_resume"] = "Continuar",
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>
    {
        // added after update 2.3.3 (racooner)
        ["os.racooner_subtitle"] = "Traversez la route, remontez le courant, remplissez les cinq terriers.",
        ["os.racooner_dens"] = "Terriers {0}/{1}",
        ["os.racooner_play"] = "Jouer",
        ["os.racooner_high_scores"] = "Meilleurs scores",
        ["os.racooner_best"] = "Record {0}",
        ["os.racooner_score"] = "Score {0}",
        ["os.racooner_level"] = "Niveau {0}",
        ["os.racooner_reached"] = "Niveau {0} atteint",
        ["os.racooner_banked"] = "Terriers remplis : {0}",
        ["os.racooner_game_over"] = "Partie terminée",
        ["os.racooner_new_record"] = "Nouveau record !",
        ["os.racooner_play_again"] = "Rejouer",
        ["os.racooner_menu"] = "Menu",
        ["os.racooner_no_scores"] = "Pas encore de score. À vous de jouer !",
        ["os.racooner_paused"] = "En pause",
        ["os.racooner_resume"] = "Reprendre",
    };

    private static readonly IReadOnlyDictionary<string, string> Pt = new Dictionary<string, string>
    {
        // added after update 2.3.3 (racooner)
        ["os.racooner_subtitle"] = "Pule pelo tráfego, atravesse a correnteza e encha as cinco tocas.",
        ["os.racooner_dens"] = "Tocas {0}/{1}",
        ["os.racooner_play"] = "Jogar",
        ["os.racooner_high_scores"] = "Melhores pontuações",
        ["os.racooner_best"] = "Recorde {0}",
        ["os.racooner_score"] = "Pontos {0}",
        ["os.racooner_level"] = "Nível {0}",
        ["os.racooner_reached"] = "Chegou ao nível {0}",
        ["os.racooner_banked"] = "Tocas cheias: {0}",
        ["os.racooner_game_over"] = "Fim de jogo",
        ["os.racooner_new_record"] = "Novo recorde!",
        ["os.racooner_play_again"] = "Jogar de novo",
        ["os.racooner_menu"] = "Menu",
        ["os.racooner_no_scores"] = "Ainda sem pontuações. Vai lá!",
        ["os.racooner_paused"] = "Pausado",
        ["os.racooner_resume"] = "Continuar",
    };

    private static readonly IReadOnlyDictionary<string, string> Ru = new Dictionary<string, string>
    {
        // added after update 2.3.3 (racooner)
        ["os.racooner_subtitle"] = "Перебеги дорогу, переплыви поток, заполни все пять нор.",
        ["os.racooner_dens"] = "Норы {0}/{1}",
        ["os.racooner_play"] = "Играть",
        ["os.racooner_high_scores"] = "Рекорды",
        ["os.racooner_best"] = "Рекорд {0}",
        ["os.racooner_score"] = "Очки {0}",
        ["os.racooner_level"] = "Уровень {0}",
        ["os.racooner_reached"] = "Дошёл до уровня {0}",
        ["os.racooner_banked"] = "Заполнено нор: {0}",
        ["os.racooner_game_over"] = "Игра окончена",
        ["os.racooner_new_record"] = "Новый рекорд!",
        ["os.racooner_play_again"] = "Ещё раз",
        ["os.racooner_menu"] = "Меню",
        ["os.racooner_no_scores"] = "Рекордов пока нет. Самое время!",
        ["os.racooner_paused"] = "Пауза",
        ["os.racooner_resume"] = "Продолжить",
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
