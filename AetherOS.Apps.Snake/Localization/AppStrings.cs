using System.Collections.Generic;

namespace AetherOS.Apps.Snake.Localization;

/// <summary>The Snake app's own UI strings, merged into the central tables at app registration.</summary>
public static class AppStrings
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        // added after update 2.1.3 (snake)
        ["os.snake_subtitle"] = "Eat. Grow. Don't bite yourself.",
        ["os.snake_play"] = "Play",
        ["os.snake_high_scores"] = "High scores",
        ["os.snake_best"] = "Best {0}",
        ["os.snake_score"] = "Score {0}",
        ["os.snake_pellets"] = "Bites: {0}",
        ["os.snake_time"] = "Survived: {0}s",
        ["os.snake_game_over"] = "Game over",
        ["os.snake_new_record"] = "New record!",
        ["os.snake_play_again"] = "Play again",
        ["os.snake_menu"] = "Menu",
        ["os.snake_no_scores"] = "No scores yet. Go get one!",
        ["os.snake_paused"] = "Paused",
        ["os.snake_resume"] = "Resume",
    };

    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>
    {
        ["os.snake_subtitle"] = "Fressen. Wachsen. Nicht selbst beißen.",
        ["os.snake_play"] = "Spielen",
        ["os.snake_high_scores"] = "Bestenliste",
        ["os.snake_best"] = "Beste {0}",
        ["os.snake_score"] = "Punkte {0}",
        ["os.snake_pellets"] = "Bissen: {0}",
        ["os.snake_time"] = "Überlebt: {0}s",
        ["os.snake_game_over"] = "Vorbei",
        ["os.snake_new_record"] = "Neuer Rekord!",
        ["os.snake_play_again"] = "Nochmal",
        ["os.snake_menu"] = "Menü",
        ["os.snake_no_scores"] = "Noch keine Punkte. Auf geht's!",
        ["os.snake_paused"] = "Pause",
        ["os.snake_resume"] = "Weiter",
    };

    private static readonly IReadOnlyDictionary<string, string> Es = new Dictionary<string, string>
    {
        ["os.snake_subtitle"] = "Come. Crece. No te muerdas.",
        ["os.snake_play"] = "Jugar",
        ["os.snake_high_scores"] = "Mejores puntuaciones",
        ["os.snake_best"] = "Mejor {0}",
        ["os.snake_score"] = "Puntos {0}",
        ["os.snake_pellets"] = "Bocados: {0}",
        ["os.snake_time"] = "Sobreviviste: {0}s",
        ["os.snake_game_over"] = "Fin de la partida",
        ["os.snake_new_record"] = "¡Nuevo récord!",
        ["os.snake_play_again"] = "Otra vez",
        ["os.snake_menu"] = "Menú",
        ["os.snake_no_scores"] = "Aún no hay puntuaciones. ¡A por ellas!",
        ["os.snake_paused"] = "En pausa",
        ["os.snake_resume"] = "Continuar",
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>
    {
        ["os.snake_subtitle"] = "Mange. Grandis. Ne te mords pas.",
        ["os.snake_play"] = "Jouer",
        ["os.snake_high_scores"] = "Meilleurs scores",
        ["os.snake_best"] = "Record {0}",
        ["os.snake_score"] = "Score {0}",
        ["os.snake_pellets"] = "Bouchées : {0}",
        ["os.snake_time"] = "Survie : {0}s",
        ["os.snake_game_over"] = "Partie terminée",
        ["os.snake_new_record"] = "Nouveau record !",
        ["os.snake_play_again"] = "Rejouer",
        ["os.snake_menu"] = "Menu",
        ["os.snake_no_scores"] = "Pas encore de score. À vous de jouer !",
        ["os.snake_paused"] = "En pause",
        ["os.snake_resume"] = "Reprendre",
    };

    private static readonly IReadOnlyDictionary<string, string> Pt = new Dictionary<string, string>
    {
        ["os.snake_subtitle"] = "Coma. Cresça. Não se morda.",
        ["os.snake_play"] = "Jogar",
        ["os.snake_high_scores"] = "Melhores pontuações",
        ["os.snake_best"] = "Recorde {0}",
        ["os.snake_score"] = "Pontos {0}",
        ["os.snake_pellets"] = "Mordidas: {0}",
        ["os.snake_time"] = "Sobreviveu: {0}s",
        ["os.snake_game_over"] = "Fim de jogo",
        ["os.snake_new_record"] = "Novo recorde!",
        ["os.snake_play_again"] = "Jogar de novo",
        ["os.snake_menu"] = "Menu",
        ["os.snake_no_scores"] = "Ainda sem pontuações. Vai lá!",
        ["os.snake_paused"] = "Pausado",
        ["os.snake_resume"] = "Continuar",
    };

    private static readonly IReadOnlyDictionary<string, string> Ru = new Dictionary<string, string>
    {
        ["os.snake_subtitle"] = "Ешь. Расти. Не кусай себя.",
        ["os.snake_play"] = "Играть",
        ["os.snake_high_scores"] = "Рекорды",
        ["os.snake_best"] = "Рекорд {0}",
        ["os.snake_score"] = "Очки {0}",
        ["os.snake_pellets"] = "Съедено: {0}",
        ["os.snake_time"] = "Продержались: {0}с",
        ["os.snake_game_over"] = "Игра окончена",
        ["os.snake_new_record"] = "Новый рекорд!",
        ["os.snake_play_again"] = "Ещё раз",
        ["os.snake_menu"] = "Меню",
        ["os.snake_no_scores"] = "Рекордов пока нет. Самое время!",
        ["os.snake_paused"] = "Пауза",
        ["os.snake_resume"] = "Продолжить",
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
