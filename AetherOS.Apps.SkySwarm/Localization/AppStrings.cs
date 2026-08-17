using System.Collections.Generic;

namespace AetherOS.Apps.SkySwarm.Localization;

/// <summary>The Sky Swarm app's own UI strings, merged into the central tables at app registration.</summary>
public static class AppStrings
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        // added after update 2.3.3 (sky swarm)
        ["os.skyswarm_subtitle"] = "Weave, shoot, rescue.",
        ["os.skyswarm_play"] = "Play",
        ["os.skyswarm_high_scores"] = "High scores",
        ["os.skyswarm_best"] = "Best {0}",
        ["os.skyswarm_score"] = "Score {0}",
        ["os.skyswarm_stage"] = "Stage {0}",
        ["os.skyswarm_reached"] = "Reached stage {0}",
        ["os.skyswarm_time"] = "Time {0}s",
        ["os.skyswarm_game_over"] = "Game over",
        ["os.skyswarm_new_record"] = "New record!",
        ["os.skyswarm_play_again"] = "Play again",
        ["os.skyswarm_menu"] = "Menu",
        ["os.skyswarm_no_scores"] = "No scores yet. Go get one!",
        ["os.skyswarm_paused"] = "Paused",
        ["os.skyswarm_resume"] = "Resume",
        ["os.skyswarm_challenge"] = "Challenge stage!",
        ["os.skyswarm_hits"] = "{0} of {1} hit",
        ["os.skyswarm_perfect"] = "Perfect! +{0}",
    };

    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>
    {
        // added after update 2.3.3 (sky swarm)
        ["os.skyswarm_subtitle"] = "Ausweichen, schießen, retten.",
        ["os.skyswarm_play"] = "Spielen",
        ["os.skyswarm_high_scores"] = "Bestenliste",
        ["os.skyswarm_best"] = "Beste {0}",
        ["os.skyswarm_score"] = "Punkte {0}",
        ["os.skyswarm_stage"] = "Stufe {0}",
        ["os.skyswarm_reached"] = "Stufe {0} erreicht",
        ["os.skyswarm_time"] = "Zeit {0}s",
        ["os.skyswarm_game_over"] = "Vorbei",
        ["os.skyswarm_new_record"] = "Neuer Rekord!",
        ["os.skyswarm_play_again"] = "Nochmal",
        ["os.skyswarm_menu"] = "Menü",
        ["os.skyswarm_no_scores"] = "Noch keine Punkte. Auf geht's!",
        ["os.skyswarm_paused"] = "Pause",
        ["os.skyswarm_resume"] = "Weiter",
        ["os.skyswarm_challenge"] = "Bonusstufe!",
        ["os.skyswarm_hits"] = "{0} von {1} getroffen",
        ["os.skyswarm_perfect"] = "Perfekt! +{0}",
    };

    private static readonly IReadOnlyDictionary<string, string> Es = new Dictionary<string, string>
    {
        // added after update 2.3.3 (sky swarm)
        ["os.skyswarm_subtitle"] = "Esquiva, dispara, rescata.",
        ["os.skyswarm_play"] = "Jugar",
        ["os.skyswarm_high_scores"] = "Mejores puntuaciones",
        ["os.skyswarm_best"] = "Mejor {0}",
        ["os.skyswarm_score"] = "Puntos {0}",
        ["os.skyswarm_stage"] = "Fase {0}",
        ["os.skyswarm_reached"] = "Llegaste a la fase {0}",
        ["os.skyswarm_time"] = "Tiempo {0}s",
        ["os.skyswarm_game_over"] = "Fin de la partida",
        ["os.skyswarm_new_record"] = "¡Nuevo récord!",
        ["os.skyswarm_play_again"] = "Otra vez",
        ["os.skyswarm_menu"] = "Menú",
        ["os.skyswarm_no_scores"] = "Aún no hay puntuaciones. ¡A por ellas!",
        ["os.skyswarm_paused"] = "En pausa",
        ["os.skyswarm_resume"] = "Continuar",
        ["os.skyswarm_challenge"] = "¡Fase de bonificación!",
        ["os.skyswarm_hits"] = "{0} de {1} derribados",
        ["os.skyswarm_perfect"] = "¡Perfecto! +{0}",
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>
    {
        // added after update 2.3.3 (sky swarm)
        ["os.skyswarm_subtitle"] = "Esquivez, tirez, sauvez.",
        ["os.skyswarm_play"] = "Jouer",
        ["os.skyswarm_high_scores"] = "Meilleurs scores",
        ["os.skyswarm_best"] = "Record {0}",
        ["os.skyswarm_score"] = "Score {0}",
        ["os.skyswarm_stage"] = "Niveau {0}",
        ["os.skyswarm_reached"] = "Niveau {0} atteint",
        ["os.skyswarm_time"] = "Temps {0}s",
        ["os.skyswarm_game_over"] = "Partie terminée",
        ["os.skyswarm_new_record"] = "Nouveau record !",
        ["os.skyswarm_play_again"] = "Rejouer",
        ["os.skyswarm_menu"] = "Menu",
        ["os.skyswarm_no_scores"] = "Pas encore de score. À vous de jouer !",
        ["os.skyswarm_paused"] = "En pause",
        ["os.skyswarm_resume"] = "Reprendre",
        ["os.skyswarm_challenge"] = "Niveau bonus !",
        ["os.skyswarm_hits"] = "{0} sur {1} touchés",
        ["os.skyswarm_perfect"] = "Parfait ! +{0}",
    };

    private static readonly IReadOnlyDictionary<string, string> Pt = new Dictionary<string, string>
    {
        // added after update 2.3.3 (sky swarm)
        ["os.skyswarm_subtitle"] = "Desvie, atire, resgate.",
        ["os.skyswarm_play"] = "Jogar",
        ["os.skyswarm_high_scores"] = "Melhores pontuações",
        ["os.skyswarm_best"] = "Recorde {0}",
        ["os.skyswarm_score"] = "Pontos {0}",
        ["os.skyswarm_stage"] = "Fase {0}",
        ["os.skyswarm_reached"] = "Chegou à fase {0}",
        ["os.skyswarm_time"] = "Tempo {0}s",
        ["os.skyswarm_game_over"] = "Fim de jogo",
        ["os.skyswarm_new_record"] = "Novo recorde!",
        ["os.skyswarm_play_again"] = "Jogar de novo",
        ["os.skyswarm_menu"] = "Menu",
        ["os.skyswarm_no_scores"] = "Ainda sem pontuações. Vai lá!",
        ["os.skyswarm_paused"] = "Pausado",
        ["os.skyswarm_resume"] = "Continuar",
        ["os.skyswarm_challenge"] = "Fase bônus!",
        ["os.skyswarm_hits"] = "{0} de {1} atingidos",
        ["os.skyswarm_perfect"] = "Perfeito! +{0}",
    };

    private static readonly IReadOnlyDictionary<string, string> Ru = new Dictionary<string, string>
    {
        // added after update 2.3.3 (sky swarm)
        ["os.skyswarm_subtitle"] = "Уворачивайся, стреляй, спасай.",
        ["os.skyswarm_play"] = "Играть",
        ["os.skyswarm_high_scores"] = "Рекорды",
        ["os.skyswarm_best"] = "Рекорд {0}",
        ["os.skyswarm_score"] = "Очки {0}",
        ["os.skyswarm_stage"] = "Этап {0}",
        ["os.skyswarm_reached"] = "Дошёл до этапа {0}",
        ["os.skyswarm_time"] = "Время {0} с",
        ["os.skyswarm_game_over"] = "Игра окончена",
        ["os.skyswarm_new_record"] = "Новый рекорд!",
        ["os.skyswarm_play_again"] = "Ещё раз",
        ["os.skyswarm_menu"] = "Меню",
        ["os.skyswarm_no_scores"] = "Рекордов пока нет. Самое время!",
        ["os.skyswarm_paused"] = "Пауза",
        ["os.skyswarm_resume"] = "Продолжить",
        ["os.skyswarm_challenge"] = "Бонусный этап!",
        ["os.skyswarm_hits"] = "Сбито {0} из {1}",
        ["os.skyswarm_perfect"] = "Идеально! +{0}",
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
