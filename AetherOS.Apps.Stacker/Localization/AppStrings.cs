using System.Collections.Generic;

namespace AetherOS.Apps.Stacker.Localization;

/// <summary>The Stacker app's own UI strings, merged into the central tables at app registration.</summary>
public static class AppStrings
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        // added after update 2.1.3 (stacker)
        ["os.stacker_subtitle"] = "Stack them. Clear them. Repeat.",
        ["os.stacker_play"] = "Play",
        ["os.stacker_high_scores"] = "High scores",
        ["os.stacker_best"] = "Best {0}",
        ["os.stacker_score"] = "Score {0}",
        ["os.stacker_level_lines"] = "Level {0}  Lines {1}",
        ["os.stacker_lines"] = "Lines: {0}",
        ["os.stacker_level"] = "Level: {0}",
        ["os.stacker_game_over"] = "Game over",
        ["os.stacker_new_record"] = "New record!",
        ["os.stacker_play_again"] = "Play again",
        ["os.stacker_menu"] = "Menu",
        ["os.stacker_no_scores"] = "No scores yet. Go get one!",
        ["os.stacker_paused"] = "Paused",
        ["os.stacker_resume"] = "Resume",
    };

    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>
    {
        ["os.stacker_subtitle"] = "Stapeln. Räumen. Wiederholen.",
        ["os.stacker_play"] = "Spielen",
        ["os.stacker_high_scores"] = "Bestenliste",
        ["os.stacker_best"] = "Beste {0}",
        ["os.stacker_score"] = "Punkte {0}",
        ["os.stacker_level_lines"] = "Level {0}  Reihen {1}",
        ["os.stacker_lines"] = "Reihen: {0}",
        ["os.stacker_level"] = "Level: {0}",
        ["os.stacker_game_over"] = "Vorbei",
        ["os.stacker_new_record"] = "Neuer Rekord!",
        ["os.stacker_play_again"] = "Nochmal",
        ["os.stacker_menu"] = "Menü",
        ["os.stacker_no_scores"] = "Noch keine Punkte. Auf geht's!",
        ["os.stacker_paused"] = "Pause",
        ["os.stacker_resume"] = "Weiter",
    };

    private static readonly IReadOnlyDictionary<string, string> Es = new Dictionary<string, string>
    {
        ["os.stacker_subtitle"] = "Apila. Elimina. Repite.",
        ["os.stacker_play"] = "Jugar",
        ["os.stacker_high_scores"] = "Mejores puntuaciones",
        ["os.stacker_best"] = "Mejor {0}",
        ["os.stacker_score"] = "Puntos {0}",
        ["os.stacker_level_lines"] = "Nivel {0}  Líneas {1}",
        ["os.stacker_lines"] = "Líneas: {0}",
        ["os.stacker_level"] = "Nivel: {0}",
        ["os.stacker_game_over"] = "Fin de la partida",
        ["os.stacker_new_record"] = "¡Nuevo récord!",
        ["os.stacker_play_again"] = "Otra vez",
        ["os.stacker_menu"] = "Menú",
        ["os.stacker_no_scores"] = "Aún no hay puntuaciones. ¡A por ellas!",
        ["os.stacker_paused"] = "En pausa",
        ["os.stacker_resume"] = "Continuar",
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>
    {
        ["os.stacker_subtitle"] = "Empilez. Effacez. Recommencez.",
        ["os.stacker_play"] = "Jouer",
        ["os.stacker_high_scores"] = "Meilleurs scores",
        ["os.stacker_best"] = "Record {0}",
        ["os.stacker_score"] = "Score {0}",
        ["os.stacker_level_lines"] = "Niveau {0}  Lignes {1}",
        ["os.stacker_lines"] = "Lignes : {0}",
        ["os.stacker_level"] = "Niveau : {0}",
        ["os.stacker_game_over"] = "Partie terminée",
        ["os.stacker_new_record"] = "Nouveau record !",
        ["os.stacker_play_again"] = "Rejouer",
        ["os.stacker_menu"] = "Menu",
        ["os.stacker_no_scores"] = "Pas encore de score. À vous de jouer !",
        ["os.stacker_paused"] = "En pause",
        ["os.stacker_resume"] = "Reprendre",
    };

    private static readonly IReadOnlyDictionary<string, string> Pt = new Dictionary<string, string>
    {
        ["os.stacker_subtitle"] = "Empilhe. Limpe. Repita.",
        ["os.stacker_play"] = "Jogar",
        ["os.stacker_high_scores"] = "Melhores pontuações",
        ["os.stacker_best"] = "Recorde {0}",
        ["os.stacker_score"] = "Pontos {0}",
        ["os.stacker_level_lines"] = "Nível {0}  Linhas {1}",
        ["os.stacker_lines"] = "Linhas: {0}",
        ["os.stacker_level"] = "Nível: {0}",
        ["os.stacker_game_over"] = "Fim de jogo",
        ["os.stacker_new_record"] = "Novo recorde!",
        ["os.stacker_play_again"] = "Jogar de novo",
        ["os.stacker_menu"] = "Menu",
        ["os.stacker_no_scores"] = "Ainda sem pontuações. Vai lá!",
        ["os.stacker_paused"] = "Pausado",
        ["os.stacker_resume"] = "Continuar",
    };

    private static readonly IReadOnlyDictionary<string, string> Ru = new Dictionary<string, string>
    {
        ["os.stacker_subtitle"] = "Складывай. Убирай. Повторяй.",
        ["os.stacker_play"] = "Играть",
        ["os.stacker_high_scores"] = "Рекорды",
        ["os.stacker_best"] = "Рекорд {0}",
        ["os.stacker_score"] = "Очки {0}",
        ["os.stacker_level_lines"] = "Уровень {0}  Линии {1}",
        ["os.stacker_lines"] = "Линий: {0}",
        ["os.stacker_level"] = "Уровень: {0}",
        ["os.stacker_game_over"] = "Игра окончена",
        ["os.stacker_new_record"] = "Новый рекорд!",
        ["os.stacker_play_again"] = "Ещё раз",
        ["os.stacker_menu"] = "Меню",
        ["os.stacker_no_scores"] = "Рекордов пока нет. Самое время!",
        ["os.stacker_paused"] = "Пауза",
        ["os.stacker_resume"] = "Продолжить",
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
