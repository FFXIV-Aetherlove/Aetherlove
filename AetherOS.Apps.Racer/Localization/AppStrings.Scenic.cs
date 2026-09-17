namespace AetherOS.Apps.Racer.Localization;

using System.Collections.Generic;

public static partial class AppStrings
{
    private static IReadOnlyDictionary<string, string> WithScenic(IReadOnlyDictionary<string, string> source, int language)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in source) result[pair.Key] = pair.Value;
        string[][] rows =
        [
            ["course_double-helix", "The Double Helix", "Die Doppelhelix", "La Doble Hélice", "La Double Hélice", "A Dupla Hélice", "Двойная спираль"],
            ["course_quarry-drop", "The Quarry Drop", "Der Steinbruchfall", "El Descenso de la Cantera", "La Descente de la Carrière", "A Descida da Pedreira", "Спуск в карьер"],
            ["course_spillway", "The Spillway", "Der Überlauf", "El Aliviadero", "Le Déversoir", "O Vertedouro", "Водосброс"],
            ["course_skybridge", "The Skybridge", "Die Himmelsbrücke", "El Puente del Cielo", "Le Pont du Ciel", "A Ponte do Céu", "Небесный мост"],
            ["course_looking-glass", "The Looking Glass", "Der Spiegel", "El Espejo", "Le Miroir", "O Espelho", "Зазеркалье"],
            ["course_grandstand-dash", "The Grandstand Dash", "Der Tribünensprint", "El Sprint de las Gradas", "Le Sprint des Tribunes", "A Corrida das Arquibancadas", "Спринт по трибунам"],
            ["sec_fork", "The fork", "Die Gabelung", "La bifurcación", "La bifurcation", "A bifurcação", "Развилка"],
            ["sec_first_crossing", "The first crossing", "Die erste Kreuzung", "El primer cruce", "Le premier croisement", "O primeiro cruzamento", "Первое пересечение"],
            ["sec_upper_road", "The upper road", "Der obere Weg", "El camino superior", "La voie haute", "A via superior", "Верхняя дорога"],
            ["sec_second_crossing", "The second crossing", "Die zweite Kreuzung", "El segundo cruce", "Le second croisement", "O segundo cruzamento", "Второе пересечение"],
            ["sec_lower_road", "The lower road", "Der untere Weg", "El camino inferior", "La voie basse", "A via inferior", "Нижняя дорога"],
            ["sec_reunion", "The reunion", "Das Wiedersehen", "El reencuentro", "Les retrouvailles", "O reencontro", "Воссоединение"],
            ["sec_quarry_floor", "The quarry floor", "Die Steinbruchsohle", "El fondo de la cantera", "Le fond de la carrière", "O fundo da pedreira", "Дно карьера"],
            ["sec_first_shelf", "The first shelf", "Die erste Terrasse", "La primera terraza", "La première terrasse", "O primeiro patamar", "Первый уступ"],
            ["sec_stone_slide", "The stone slide", "Die Steinrutsche", "El tobogán de piedra", "La glissière de pierre", "O escorrega de pedra", "Каменный спуск"],
            ["sec_lower_shelf", "The lower shelf", "Die untere Terrasse", "La terraza inferior", "La terrasse basse", "O patamar inferior", "Нижний уступ"],
            ["sec_daylight_ramp", "The daylight ramp", "Die Rampe ins Licht", "La rampa hacia la luz", "La rampe vers la lumière", "A rampa para a luz", "Подъём к свету"],
            ["sec_aqueduct", "The aqueduct", "Der Aquädukt", "El acueducto", "L’aqueduc", "O aqueduto", "Акведук"],
            ["sec_first_meander", "The first meander", "Die erste Flussschleife", "El primer meandro", "Le premier méandre", "O primeiro meandro", "Первая излучина"],
            ["sec_falling_curtain", "The falling curtain", "Der Wasservorhang", "La cortina de agua", "Le rideau d’eau", "A cortina de água", "Водяная завеса"],
            ["sec_great_wheel", "The great wheel", "Das große Rad", "La gran rueda", "La grande roue", "A grande roda", "Большое колесо"],
            ["sec_causeway", "The causeway", "Der Dammweg", "La calzada", "La chaussée", "A passagem elevada", "Дамба"],
            ["sec_far_bank", "The far bank", "Das ferne Ufer", "La orilla lejana", "La rive lointaine", "A margem distante", "Дальний берег"],
            ["sec_shallows", "The shallows", "Das Flachwasser", "Las aguas someras", "Les hauts-fonds", "As águas rasas", "Мелководье"],
            ["sec_home_bank", "The home bank", "Das Heimatufer", "La orilla de regreso", "La rive du retour", "A margem de volta", "Родной берег"],
            ["sec_high_road", "The high road", "Der Höhenweg", "El camino alto", "La route des hauteurs", "O caminho alto", "Высокая дорога"],
            ["sec_ridge_sweep", "The ridge sweep", "Der Gratbogen", "La curva de la cresta", "La courbe de la crête", "A curva da crista", "Поворот на хребте"],
            ["sec_hanging_span", "The hanging span", "Die Hängebrücke", "El tramo colgante", "La travée suspendue", "O vão suspenso", "Подвесной пролёт"],
            ["sec_open_shoulder", "The open shoulder", "Die offene Flanke", "La ladera abierta", "Le flanc dégagé", "A encosta aberta", "Открытый склон"],
            ["sec_sky_shelf", "The sky shelf", "Die Himmelsterrasse", "La terraza del cielo", "La terrasse du ciel", "O patamar do céu", "Небесный уступ"],
            ["sec_stone_arch", "The stone arch", "Der Steinbogen", "El arco de piedra", "L’arche de pierre", "O arco de pedra", "Каменная арка"],
            ["sec_long_gust", "The long gust", "Die lange Böe", "La larga ráfaga", "La longue rafale", "A longa rajada", "Долгий порыв"],
            ["sec_lee", "The lee", "Der Windschatten", "El abrigo", "L’abri du vent", "O abrigo do vento", "Затишье"],
            ["sec_frozen_lake", "The frozen lake", "Der gefrorene See", "El lago helado", "Le lac gelé", "O lago congelado", "Замёрзшее озеро"],
            ["sec_frozen_shore", "The frozen shore", "Das gefrorene Ufer", "La orilla helada", "La rive gelée", "A margem congelada", "Замёрзший берег"],
            ["sec_crystal_tunnel", "The crystal tunnel", "Der Kristalltunnel", "El túnel de cristal", "Le tunnel de cristal", "O túnel de cristal", "Кристальный тоннель"],
            ["sec_pale_curve", "The pale curve", "Die blasse Kurve", "La curva pálida", "La courbe pâle", "A curva pálida", "Бледный поворот"],
            ["sec_still_expanse", "The still expanse", "Die stille Weite", "La extensión serena", "L’étendue immobile", "A vastidão serena", "Тихий простор"],
            ["sec_far_shore", "The far shore", "Das ferne Gestade", "La costa lejana", "Le rivage lointain", "A costa distante", "Дальнее побережье"],
            ["sec_ice_road", "The ice road", "Die Eisstraße", "El camino de hielo", "La route de glace", "A estrada de gelo", "Ледяная дорога"],
            ["sec_thaw", "The thaw", "Das Tauwetter", "El deshielo", "Le dégel", "O degelo", "Оттепель"],
            ["sec_festival_green", "The festival green", "Die Festwiese", "El prado festivo", "La pelouse de fête", "O campo da festa", "Праздничная поляна"],
            ["sec_cheering_stands", "The cheering stands", "Die jubelnden Tribünen", "Las gradas animadas", "Les tribunes en liesse", "As arquibancadas animadas", "Ликующие трибуны"],
            ["sec_underpass", "The underpass", "Die Unterführung", "El paso inferior", "Le passage inférieur", "A passagem inferior", "Проезд под трибунами"],
            ["sec_arena_entrance", "The arena entrance", "Der Arenaeingang", "La entrada a la arena", "L’entrée de l’arène", "A entrada da arena", "Вход на арену"],
        ];
        foreach (var row in rows)
        {
            result["os.racer_" + row[0]] = row[language + 1];
        }
        return result;
    }
}
