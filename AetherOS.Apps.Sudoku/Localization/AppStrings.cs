using System.Collections.Generic;

namespace AetherOS.Apps.Sudoku.Localization;

/// <summary>The Sudoku app's own UI strings, merged into the central tables at app registration.</summary>
public static class AppStrings
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        // added after update 2.2.3 (sudoku)
        ["os.sudoku_subtitle"] = "Every grid you clear hands you a harder one.",
        ["os.sudoku_play"] = "Play",
        ["os.sudoku_high_scores"] = "High scores",
        ["os.sudoku_best"] = "Best {0}",
        ["os.sudoku_score"] = "Score {0}",
        ["os.sudoku_easy"] = "Easy",
        ["os.sudoku_medium"] = "Medium",
        ["os.sudoku_difficult"] = "Difficult",
        ["os.sudoku_insane"] = "Insane",
        ["os.sudoku_generating"] = "Building a grid...",
        ["os.sudoku_cleared"] = "Grid cleared",
        ["os.sudoku_solved_count"] = "Solved: {0}",
        ["os.sudoku_next"] = "Next: {0}",
        ["os.sudoku_next_button"] = "Next grid",
        ["os.sudoku_reached"] = "Reached: {0}",
        ["os.sudoku_out_of_time"] = "Out of time.",
        ["os.sudoku_out_of_strikes"] = "Three mistakes.",
        ["os.sudoku_paused"] = "Paused",
        ["os.sudoku_resume"] = "Resume",
        ["os.sudoku_game_over"] = "Run over",
        ["os.sudoku_new_record"] = "New record!",
        ["os.sudoku_play_again"] = "Play again",
        ["os.sudoku_menu"] = "Menu",
        ["os.sudoku_no_scores"] = "No scores yet. Go get one!",
    };

    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>
    {
        // added after update 2.2.3 (sudoku)
        ["os.sudoku_subtitle"] = "Jedes gelöste Gitter bringt ein schwereres.",
        ["os.sudoku_play"] = "Spielen",
        ["os.sudoku_high_scores"] = "Bestenliste",
        ["os.sudoku_best"] = "Beste {0}",
        ["os.sudoku_score"] = "Punkte {0}",
        ["os.sudoku_easy"] = "Leicht",
        ["os.sudoku_medium"] = "Mittel",
        ["os.sudoku_difficult"] = "Schwer",
        ["os.sudoku_insane"] = "Irrsinnig",
        ["os.sudoku_generating"] = "Gitter wird gebaut...",
        ["os.sudoku_cleared"] = "Gitter gelöst",
        ["os.sudoku_solved_count"] = "Gelöst: {0}",
        ["os.sudoku_next"] = "Nächstes: {0}",
        ["os.sudoku_next_button"] = "Nächstes Gitter",
        ["os.sudoku_reached"] = "Erreicht: {0}",
        ["os.sudoku_out_of_time"] = "Zeit abgelaufen.",
        ["os.sudoku_out_of_strikes"] = "Drei Fehler.",
        ["os.sudoku_paused"] = "Pause",
        ["os.sudoku_resume"] = "Weiter",
        ["os.sudoku_game_over"] = "Lauf vorbei",
        ["os.sudoku_new_record"] = "Neuer Rekord!",
        ["os.sudoku_play_again"] = "Nochmal",
        ["os.sudoku_menu"] = "Menü",
        ["os.sudoku_no_scores"] = "Noch keine Punkte. Auf geht's!",
    };

    private static readonly IReadOnlyDictionary<string, string> Es = new Dictionary<string, string>
    {
        // added after update 2.2.3 (sudoku)
        ["os.sudoku_subtitle"] = "Cada cuadrícula resuelta te trae una más difícil.",
        ["os.sudoku_play"] = "Jugar",
        ["os.sudoku_high_scores"] = "Mejores puntuaciones",
        ["os.sudoku_best"] = "Mejor {0}",
        ["os.sudoku_score"] = "Puntos {0}",
        ["os.sudoku_easy"] = "Fácil",
        ["os.sudoku_medium"] = "Media",
        ["os.sudoku_difficult"] = "Difícil",
        ["os.sudoku_insane"] = "Demencial",
        ["os.sudoku_generating"] = "Creando cuadrícula...",
        ["os.sudoku_cleared"] = "Cuadrícula resuelta",
        ["os.sudoku_solved_count"] = "Resueltas: {0}",
        ["os.sudoku_next"] = "Siguiente: {0}",
        ["os.sudoku_next_button"] = "Siguiente cuadrícula",
        ["os.sudoku_reached"] = "Alcanzado: {0}",
        ["os.sudoku_out_of_time"] = "Se acabó el tiempo.",
        ["os.sudoku_out_of_strikes"] = "Tres errores.",
        ["os.sudoku_paused"] = "En pausa",
        ["os.sudoku_resume"] = "Continuar",
        ["os.sudoku_game_over"] = "Partida terminada",
        ["os.sudoku_new_record"] = "¡Nuevo récord!",
        ["os.sudoku_play_again"] = "Jugar de nuevo",
        ["os.sudoku_menu"] = "Menú",
        ["os.sudoku_no_scores"] = "Aún no hay puntuaciones. ¡A por una!",
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>
    {
        // added after update 2.2.3 (sudoku)
        ["os.sudoku_subtitle"] = "Chaque grille résolue en amène une plus dure.",
        ["os.sudoku_play"] = "Jouer",
        ["os.sudoku_high_scores"] = "Meilleurs scores",
        ["os.sudoku_best"] = "Record {0}",
        ["os.sudoku_score"] = "Score {0}",
        ["os.sudoku_easy"] = "Facile",
        ["os.sudoku_medium"] = "Moyen",
        ["os.sudoku_difficult"] = "Difficile",
        ["os.sudoku_insane"] = "Démentiel",
        ["os.sudoku_generating"] = "Création de la grille...",
        ["os.sudoku_cleared"] = "Grille résolue",
        ["os.sudoku_solved_count"] = "Résolues : {0}",
        ["os.sudoku_next"] = "Suivante : {0}",
        ["os.sudoku_next_button"] = "Grille suivante",
        ["os.sudoku_reached"] = "Atteint : {0}",
        ["os.sudoku_out_of_time"] = "Temps écoulé.",
        ["os.sudoku_out_of_strikes"] = "Trois erreurs.",
        ["os.sudoku_paused"] = "En pause",
        ["os.sudoku_resume"] = "Reprendre",
        ["os.sudoku_game_over"] = "Partie terminée",
        ["os.sudoku_new_record"] = "Nouveau record !",
        ["os.sudoku_play_again"] = "Rejouer",
        ["os.sudoku_menu"] = "Menu",
        ["os.sudoku_no_scores"] = "Pas encore de score. À toi de jouer !",
    };

    private static readonly IReadOnlyDictionary<string, string> Pt = new Dictionary<string, string>
    {
        // added after update 2.2.3 (sudoku)
        ["os.sudoku_subtitle"] = "Cada grelha resolvida traz uma mais difícil.",
        ["os.sudoku_play"] = "Jogar",
        ["os.sudoku_high_scores"] = "Melhores pontuações",
        ["os.sudoku_best"] = "Melhor {0}",
        ["os.sudoku_score"] = "Pontos {0}",
        ["os.sudoku_easy"] = "Fácil",
        ["os.sudoku_medium"] = "Média",
        ["os.sudoku_difficult"] = "Difícil",
        ["os.sudoku_insane"] = "Insana",
        ["os.sudoku_generating"] = "A criar a grelha...",
        ["os.sudoku_cleared"] = "Grelha resolvida",
        ["os.sudoku_solved_count"] = "Resolvidas: {0}",
        ["os.sudoku_next"] = "Seguinte: {0}",
        ["os.sudoku_next_button"] = "Grelha seguinte",
        ["os.sudoku_reached"] = "Alcançado: {0}",
        ["os.sudoku_out_of_time"] = "Tempo esgotado.",
        ["os.sudoku_out_of_strikes"] = "Três erros.",
        ["os.sudoku_paused"] = "Em pausa",
        ["os.sudoku_resume"] = "Continuar",
        ["os.sudoku_game_over"] = "Fim da partida",
        ["os.sudoku_new_record"] = "Novo recorde!",
        ["os.sudoku_play_again"] = "Jogar de novo",
        ["os.sudoku_menu"] = "Menu",
        ["os.sudoku_no_scores"] = "Ainda sem pontuações. Vai buscar uma!",
    };

    private static readonly IReadOnlyDictionary<string, string> Ru = new Dictionary<string, string>
    {
        // added after update 2.2.3 (sudoku)
        ["os.sudoku_subtitle"] = "Каждая решённая сетка подкидывает следующую, потруднее.",
        ["os.sudoku_play"] = "Играть",
        ["os.sudoku_high_scores"] = "Рекорды",
        ["os.sudoku_best"] = "Рекорд {0}",
        ["os.sudoku_score"] = "Очки {0}",
        ["os.sudoku_easy"] = "Легко",
        ["os.sudoku_medium"] = "Средне",
        ["os.sudoku_difficult"] = "Сложно",
        ["os.sudoku_insane"] = "Безумно",
        ["os.sudoku_generating"] = "Собираем сетку...",
        ["os.sudoku_cleared"] = "Сетка решена",
        ["os.sudoku_solved_count"] = "Решено: {0}",
        ["os.sudoku_next"] = "Дальше: {0}",
        ["os.sudoku_next_button"] = "Следующая сетка",
        ["os.sudoku_reached"] = "Дошёл до: {0}",
        ["os.sudoku_out_of_time"] = "Время вышло.",
        ["os.sudoku_out_of_strikes"] = "Три ошибки.",
        ["os.sudoku_paused"] = "Пауза",
        ["os.sudoku_resume"] = "Продолжить",
        ["os.sudoku_game_over"] = "Забег окончен",
        ["os.sudoku_new_record"] = "Новый рекорд!",
        ["os.sudoku_play_again"] = "Ещё раз",
        ["os.sudoku_menu"] = "Меню",
        ["os.sudoku_no_scores"] = "Рекордов пока нет. Пора это исправить!",
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
