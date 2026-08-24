using System.Collections.Generic;

namespace AetherOS.Apps.Together.Localization;

/// <summary>The Together app's own UI strings, merged into the central tables at app registration. The
/// party strings themselves (os.party_*) stay central: the shell's surfaces speak them too.</summary>
public static class AppStrings
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        // added after update 2.4.0 (together app)
        ["os.together_tagline"] = "Play with friends. Earn more sparks together.",
        ["os.together_offline"] = "The party needs the server. Check your connection and try again.",
        ["os.together_solo_body"] = "Start a party and share the code. Friends join with it. Hunts, watch parties and chat then happen together.",
        ["os.together_join_title"] = "Have a code?",
        ["os.together_how"] = "How does this work?",
        ["os.together_you"] = "{0} (you)",
        ["os.together_tour_f1_app"] = "This app is the front door: start, join, invite and leave from here.",
        ["os.together_tour_sparks_title"] = "Together pays more",
        ["os.together_tour_sparks_body"] = "Some sparks only drop when you play with a party.",
        ["os.together_tour_sparks_wayfinder"] = "A party hunt in Wayfinder pays 5 extra sparks for every friend who finds the place too.",
        ["os.together_tour_sparks_echo"] = "Hosting an Echo watch party pays 2 sparks. Joining one pays 1.",
        ["os.together_tour_sparks_wallet"] = "The Wallet lists every way to earn and what you made this week.",
        ["os.together_tour_settings_body"] = "Choose what you see and what your friends see. You can change this any time.",
    };

    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>
    {
        // added after update 2.4.0 (together app)
        ["os.together_tagline"] = "Spiel mit Freunden. Verdient zusammen mehr Sparks.",
        ["os.together_offline"] = "Die Gruppe braucht den Server. Prüf deine Verbindung und versuch es nochmal.",
        ["os.together_solo_body"] = "Starte eine Gruppe und teile den Code. Freunde treten damit bei. Jagden, Watch-Partys und Chat passieren dann gemeinsam.",
        ["os.together_join_title"] = "Hast du einen Code?",
        ["os.together_how"] = "Wie funktioniert das?",
        ["os.together_you"] = "{0} (du)",
        ["os.together_tour_f1_app"] = "Diese App ist der Eingang: hier startest du, trittst bei, lädst ein und gehst wieder.",
        ["os.together_tour_sparks_title"] = "Zusammen gibt es mehr",
        ["os.together_tour_sparks_body"] = "Manche Sparks gibt es nur, wenn du mit einer Gruppe spielst.",
        ["os.together_tour_sparks_wayfinder"] = "Eine Gruppenjagd in Wayfinder zahlt 5 Sparks extra für jeden Freund, der den Ort auch findet.",
        ["os.together_tour_sparks_echo"] = "Eine Echo-Watch-Party zu hosten bringt 2 Sparks. Beitreten bringt 1.",
        ["os.together_tour_sparks_wallet"] = "Das Wallet zeigt jede Möglichkeit zu verdienen und was du diese Woche geschafft hast.",
        ["os.together_tour_settings_body"] = "Entscheide, was du siehst und was deine Freunde sehen. Du kannst das jederzeit ändern.",
    };

    private static readonly IReadOnlyDictionary<string, string> Es = new Dictionary<string, string>
    {
        // added after update 2.4.0 (together app)
        ["os.together_tagline"] = "Juega con amigos. Ganad más sparks juntos.",
        ["os.together_offline"] = "El grupo necesita el servidor. Revisa tu conexión e inténtalo otra vez.",
        ["os.together_solo_body"] = "Crea un grupo y comparte el código. Tus amigos entran con él. Las búsquedas, las fiestas de vídeo y el chat pasan entonces en grupo.",
        ["os.together_join_title"] = "¿Tienes un código?",
        ["os.together_how"] = "¿Cómo funciona?",
        ["os.together_you"] = "{0} (tú)",
        ["os.together_tour_f1_app"] = "Esta app es la puerta de entrada: aquí creas, te unes, invitas y sales.",
        ["os.together_tour_sparks_title"] = "Juntos se gana más",
        ["os.together_tour_sparks_body"] = "Algunos sparks solo caen cuando juegas con un grupo.",
        ["os.together_tour_sparks_wayfinder"] = "Una búsqueda en grupo de Wayfinder paga 5 sparks extra por cada amigo que también encuentra el sitio.",
        ["os.together_tour_sparks_echo"] = "Organizar una fiesta de vídeo en Echo paga 2 sparks. Unirse paga 1.",
        ["os.together_tour_sparks_wallet"] = "La Cartera muestra todas las formas de ganar y lo que llevas esta semana.",
        ["os.together_tour_settings_body"] = "Elige qué ves tú y qué ven tus amigos. Puedes cambiarlo cuando quieras.",
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>
    {
        // added after update 2.4.0 (together app)
        ["os.together_tagline"] = "Jouez entre amis. Gagnez plus de sparks ensemble.",
        ["os.together_offline"] = "Le groupe a besoin du serveur. Vérifiez votre connexion et réessayez.",
        ["os.together_solo_body"] = "Créez un groupe et partagez le code. Vos amis le rejoignent avec. Chasses, soirées vidéo et discussion se font alors ensemble.",
        ["os.together_join_title"] = "Vous avez un code ?",
        ["os.together_how"] = "Comment ça marche ?",
        ["os.together_you"] = "{0} (vous)",
        ["os.together_tour_f1_app"] = "Cette appli est la porte d'entrée : créez, rejoignez, invitez et quittez depuis ici.",
        ["os.together_tour_sparks_title"] = "Ensemble, ça rapporte plus",
        ["os.together_tour_sparks_body"] = "Certains sparks ne tombent que quand vous jouez avec un groupe.",
        ["os.together_tour_sparks_wayfinder"] = "Une chasse de groupe dans Wayfinder rapporte 5 sparks de plus pour chaque ami qui trouve aussi le lieu.",
        ["os.together_tour_sparks_echo"] = "Organiser une soirée vidéo Echo rapporte 2 sparks. La rejoindre en rapporte 1.",
        ["os.together_tour_sparks_wallet"] = "Le Portefeuille liste chaque façon de gagner et ce que vous avez fait cette semaine.",
        ["os.together_tour_settings_body"] = "Choisissez ce que vous voyez et ce que vos amis voient. Vous pouvez changer à tout moment.",
    };

    private static readonly IReadOnlyDictionary<string, string> Pt = new Dictionary<string, string>
    {
        // added after update 2.4.0 (together app)
        ["os.together_tagline"] = "Joga com amigos. Ganhem mais sparks juntos.",
        ["os.together_offline"] = "O grupo precisa do servidor. Verifica a tua ligação e tenta outra vez.",
        ["os.together_solo_body"] = "Cria um grupo e partilha o código. Os amigos entram com ele. Caças, sessões de vídeo e chat passam a acontecer em grupo.",
        ["os.together_join_title"] = "Tens um código?",
        ["os.together_how"] = "Como funciona?",
        ["os.together_you"] = "{0} (tu)",
        ["os.together_tour_f1_app"] = "Esta app é a porta de entrada: cria, entra, convida e sai a partir daqui.",
        ["os.together_tour_sparks_title"] = "Juntos rende mais",
        ["os.together_tour_sparks_body"] = "Alguns sparks só caem quando jogas com um grupo.",
        ["os.together_tour_sparks_wayfinder"] = "Uma caça em grupo no Wayfinder paga 5 sparks extra por cada amigo que também encontra o lugar.",
        ["os.together_tour_sparks_echo"] = "Organizar uma sessão de vídeo no Echo paga 2 sparks. Entrar numa paga 1.",
        ["os.together_tour_sparks_wallet"] = "A Carteira mostra todas as formas de ganhar e o que fizeste esta semana.",
        ["os.together_tour_settings_body"] = "Escolhe o que vês e o que os teus amigos veem. Podes mudar isto quando quiseres.",
    };

    private static readonly IReadOnlyDictionary<string, string> Ru = new Dictionary<string, string>
    {
        // added after update 2.4.0 (together app)
        ["os.together_tagline"] = "Играй с друзьями. Вместе спарков больше.",
        ["os.together_offline"] = "Отряду нужен сервер. Проверь соединение и попробуй ещё раз.",
        ["os.together_solo_body"] = "Создай отряд и поделись кодом. Друзья входят по нему. Охота, совместный просмотр и чат дальше идут вместе.",
        ["os.together_join_title"] = "Есть код?",
        ["os.together_how"] = "Как это работает?",
        ["os.together_you"] = "{0} (ты)",
        ["os.together_tour_f1_app"] = "Это приложение и есть вход: отсюда создаёшь, входишь, приглашаешь и выходишь.",
        ["os.together_tour_sparks_title"] = "Вместе платят больше",
        ["os.together_tour_sparks_body"] = "Часть спарков выпадает только за игру в отряде.",
        ["os.together_tour_sparks_wayfinder"] = "Отрядная охота в Wayfinder даёт 5 спарков сверху за каждого друга, который тоже нашёл место.",
        ["os.together_tour_sparks_echo"] = "Хост совместного просмотра в Echo получает 2 спарка. Гость получает 1.",
        ["os.together_tour_sparks_wallet"] = "Кошелёк показывает все способы заработать и сколько ты получил за неделю.",
        ["os.together_tour_settings_body"] = "Выбери, что видишь ты и что видят друзья. Это можно поменять в любой момент.",
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
