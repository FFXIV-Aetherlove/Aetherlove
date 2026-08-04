using System.Collections.Generic;

namespace AetherOS.Apps.MeteorCommand.Localization;

/// <summary>The Meteor Command app's own UI strings, merged into the central tables at app registration.</summary>
public static class AppStrings
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        // added after update 2.1.3 (meteor command)
        ["os.meteor_subtitle"] = "Tap the sky. Save the towns.",
        ["os.meteor_play"] = "Play",
        ["os.meteor_high_scores"] = "High scores",
        ["os.meteor_best"] = "Best {0}",
        ["os.meteor_score"] = "Score {0}",
        ["os.meteor_wave"] = "Wave {0}",
        ["os.meteor_ammo"] = "Ammo {0}",
        ["os.meteor_wave_clear"] = "Wave clear  +{0}",
        ["os.meteor_reached"] = "Reached wave {0}",
        ["os.meteor_game_over"] = "Game over",
        ["os.meteor_new_record"] = "New record!",
        ["os.meteor_play_again"] = "Play again",
        ["os.meteor_menu"] = "Menu",
        ["os.meteor_no_scores"] = "No scores yet. Go get one!",
        ["os.meteor_paused"] = "Paused",
        ["os.meteor_resume"] = "Resume",
    };

    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>
    {
        ["os.meteor_subtitle"] = "Tippe in den Himmel. Rette die Städte.",
        ["os.meteor_play"] = "Spielen",
        ["os.meteor_high_scores"] = "Bestenliste",
        ["os.meteor_best"] = "Beste {0}",
        ["os.meteor_score"] = "Punkte {0}",
        ["os.meteor_wave"] = "Welle {0}",
        ["os.meteor_ammo"] = "Munition {0}",
        ["os.meteor_wave_clear"] = "Welle geschafft  +{0}",
        ["os.meteor_reached"] = "Welle {0} erreicht",
        ["os.meteor_game_over"] = "Vorbei",
        ["os.meteor_new_record"] = "Neuer Rekord!",
        ["os.meteor_play_again"] = "Nochmal",
        ["os.meteor_menu"] = "Menü",
        ["os.meteor_no_scores"] = "Noch keine Punkte. Auf geht's!",
        ["os.meteor_paused"] = "Pause",
        ["os.meteor_resume"] = "Weiter",
    };

    private static readonly IReadOnlyDictionary<string, string> Es = new Dictionary<string, string>
    {
        ["os.meteor_subtitle"] = "Toca el cielo. Salva los pueblos.",
        ["os.meteor_play"] = "Jugar",
        ["os.meteor_high_scores"] = "Mejores puntuaciones",
        ["os.meteor_best"] = "Mejor {0}",
        ["os.meteor_score"] = "Puntos {0}",
        ["os.meteor_wave"] = "Oleada {0}",
        ["os.meteor_ammo"] = "Munición {0}",
        ["os.meteor_wave_clear"] = "Oleada superada  +{0}",
        ["os.meteor_reached"] = "Llegaste a la oleada {0}",
        ["os.meteor_game_over"] = "Fin de la partida",
        ["os.meteor_new_record"] = "¡Nuevo récord!",
        ["os.meteor_play_again"] = "Otra vez",
        ["os.meteor_menu"] = "Menú",
        ["os.meteor_no_scores"] = "Aún no hay puntuaciones. ¡A por ellas!",
        ["os.meteor_paused"] = "En pausa",
        ["os.meteor_resume"] = "Continuar",
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>
    {
        ["os.meteor_subtitle"] = "Touchez le ciel. Sauvez les villes.",
        ["os.meteor_play"] = "Jouer",
        ["os.meteor_high_scores"] = "Meilleurs scores",
        ["os.meteor_best"] = "Record {0}",
        ["os.meteor_score"] = "Score {0}",
        ["os.meteor_wave"] = "Vague {0}",
        ["os.meteor_ammo"] = "Munitions {0}",
        ["os.meteor_wave_clear"] = "Vague terminée  +{0}",
        ["os.meteor_reached"] = "Vague {0} atteinte",
        ["os.meteor_game_over"] = "Partie terminée",
        ["os.meteor_new_record"] = "Nouveau record !",
        ["os.meteor_play_again"] = "Rejouer",
        ["os.meteor_menu"] = "Menu",
        ["os.meteor_no_scores"] = "Pas encore de score. À vous de jouer !",
        ["os.meteor_paused"] = "En pause",
        ["os.meteor_resume"] = "Reprendre",
    };

    private static readonly IReadOnlyDictionary<string, string> Pt = new Dictionary<string, string>
    {
        ["os.meteor_subtitle"] = "Toque no céu. Salve as cidades.",
        ["os.meteor_play"] = "Jogar",
        ["os.meteor_high_scores"] = "Melhores pontuações",
        ["os.meteor_best"] = "Recorde {0}",
        ["os.meteor_score"] = "Pontos {0}",
        ["os.meteor_wave"] = "Onda {0}",
        ["os.meteor_ammo"] = "Munição {0}",
        ["os.meteor_wave_clear"] = "Onda concluída  +{0}",
        ["os.meteor_reached"] = "Chegou à onda {0}",
        ["os.meteor_game_over"] = "Fim de jogo",
        ["os.meteor_new_record"] = "Novo recorde!",
        ["os.meteor_play_again"] = "Jogar de novo",
        ["os.meteor_menu"] = "Menu",
        ["os.meteor_no_scores"] = "Ainda sem pontuações. Vai lá!",
        ["os.meteor_paused"] = "Pausado",
        ["os.meteor_resume"] = "Continuar",
    };

    private static readonly IReadOnlyDictionary<string, string> Ru = new Dictionary<string, string>
    {
        ["os.meteor_subtitle"] = "Бей по небу. Спасай города.",
        ["os.meteor_play"] = "Играть",
        ["os.meteor_high_scores"] = "Рекорды",
        ["os.meteor_best"] = "Рекорд {0}",
        ["os.meteor_score"] = "Очки {0}",
        ["os.meteor_wave"] = "Волна {0}",
        ["os.meteor_ammo"] = "Заряды {0}",
        ["os.meteor_wave_clear"] = "Волна пройдена  +{0}",
        ["os.meteor_reached"] = "Дошёл до волны {0}",
        ["os.meteor_game_over"] = "Игра окончена",
        ["os.meteor_new_record"] = "Новый рекорд!",
        ["os.meteor_play_again"] = "Ещё раз",
        ["os.meteor_menu"] = "Меню",
        ["os.meteor_no_scores"] = "Рекордов пока нет. Самое время!",
        ["os.meteor_paused"] = "Пауза",
        ["os.meteor_resume"] = "Продолжить",
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
