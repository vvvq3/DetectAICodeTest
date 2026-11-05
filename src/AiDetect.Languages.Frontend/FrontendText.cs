using System.Text.RegularExpressions;

namespace AiDetect.Languages.Frontend;

/// <summary>Словари и регулярные выражения для комментариев, имён и строк фронтенд-кода.</summary>
internal static partial class FrontendText
{
    public static readonly HashSet<string> Misspellings = new(StringComparer.Ordinal)
    {
        "recieve", "recieved", "reciever", "seperate", "seperated", "occured", "occurence", "lenght", "adress", "adresses", "sucess", "succesful",
        "successfull", "sucessful", "retrive", "retreive", "paramter", "paramters", "responce", "reponse", "neccessary", "necesary", "existance",
        "calender", "comming", "begining", "untill", "wich", "teh", "acces", "accesible", "proccess", "proccessing", "procces", "aditional",
        "additonal", "allready", "alredy", "arguement", "assigment", "atribute", "beggining", "buisness", "bussiness", "cancelation", "comparsion",
        "compatable", "completly", "conection", "consistant", "containg", "contructor", "convertion", "corect", "curent", "currenty", "defualt",
        "definately", "dependancy", "dependancies", "desription", "destory", "diffrent", "dissable", "enviroment", "exeption", "existant",
        "failded", "fucntion", "funtion", "garantee", "hanlder", "heigth", "identifer", "immediatly", "implmentation", "independant",
        "infomation", "initalize", "intial", "lenth", "maximun", "mesage", "messege", "occuring", "overriden", "paremeter", "perfomance",
        "permision", "posible", "prefered", "privilage", "probaly", "propery", "proprety", "recursivly", "refered", "refernce", "registred",
        "relevent", "remaing", "requred", "resouce", "resourse", "respone", "seach", "sepcific", "settigns", "similiar", "specfic", "statment",
        "stauts", "strign", "succeded", "suport", "temporay", "threshhold", "udpate", "usefull", "valdiate", "validaton", "verfiy", "wether",
        "whith", "writting", "ammount", "amout", "prodcut", "quanity", "summery", "visable", "visibilty", "hight", "widht", "colapse", "collaps",
        "dissapear", "dropdwon", "dorpdown", "selcted", "selecetd", "submited", "componet", "compnent", "elment", "elemnt", "buton", "bttn",
        "lable", "chekbox", "checbox", "placholder", "plaeholder", "toogle", "togle", "tooltipe", "modale", "redirct", "subscibe", "unsubcribe",
    };

    public static readonly HashSet<string> TranslitRoots = new(StringComparer.Ordinal)
    {
        "zakaz", "zakazy", "zakazov", "polzovatel", "polzovatelya", "polzovateli", "tovar", "tovary", "tovarov", "klient", "klienta", "dogovor",
        "ostatok", "ostatki", "kolvo", "kolichestvo", "summa", "summy", "spisok", "nomer", "schet", "scheta", "oplata", "oplaty", "sklad",
        "otchet", "otcheta", "dokument", "postavshik", "sotrudnik", "podrazdelenie", "naimenovanie", "cena", "ceny", "skidka", "dostavka",
        "familiya", "imya", "otchestvo", "itogo", "vsego", "vremya", "nachalo", "konec", "spravochnik", "nakladnaya", "prihod", "rashod",
        "pokupatel", "prodavec", "zayavka", "zadacha", "otvet", "zapros", "proverka", "oshibka", "soobshenie", "uvedomlenie", "knopka", "okno",
        "stranica", "forma", "pole", "spisok", "tablica", "vybor", "poisk", "filtr", "zagruzka",
    };

    /// <summary>Глаголы «повествовательных» комментариев: «// Fetch users», «// Handle form submission».</summary>
    public static readonly HashSet<string> NarrativeVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "validate", "check", "create", "map", "ensure", "get", "retrieve", "calculate", "build", "return", "initialize", "init", "update", "add",
        "remove", "set", "load", "save", "handle", "process", "convert", "parse", "apply", "filter", "log", "throw", "verify", "fetch", "send",
        "register", "configure", "compute", "find", "determine", "prepare", "store", "persist", "notify", "generate", "normalize", "extract",
        "resolve", "attempt", "try", "skip", "use", "call", "invoke", "wait", "mark", "append", "clear", "reset", "sort", "group", "merge",
        "assign", "fall", "default", "only", "first", "then", "finally", "next", "otherwise", "early", "guard", "cap", "limit", "clamp",
        "round", "wrap", "cleanup", "clean", "emit", "dispatch", "schedule", "copy", "match", "lookup", "look", "collect", "aggregate",
        "refresh", "sync", "trim", "split", "combine", "keep", "render", "show", "hide", "toggle", "open", "close", "focus", "scroll",
        "navigate", "redirect", "debounce", "throttle", "subscribe", "unsubscribe", "cancel", "abort", "listen", "prevent", "track", "display",
        "format", "highlight", "select", "reload", "submit", "close", "animate", "calculate", "memoize", "restore", "redirect", "close",
        "mock", "simulate", "expect", "assert", "setup", "arrange", "act", "verify", "wrapper", "helper", "state", "styles", "props", "types",
        "constants", "loading", "error", "empty", "header", "footer", "main", "sidebar", "content", "actions", "form", "list", "modal",
    };

    public static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "to", "of", "for", "and", "or", "in", "on", "by", "with", "from", "is", "it", "its", "this", "that", "be", "as", "at",
        "if", "we", "our", "new", "into", "all", "any", "not", "no",
    };

    public static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "cfg", "conf", "mgr", "svc", "srv", "req", "res", "resp", "msg", "tmp", "temp", "cnt", "idx", "str", "obj", "arr", "lst", "dt", "val",
        "num", "btn", "usr", "pwd", "conn", "cmd", "prm", "ctx", "dict", "col", "elem", "el", "doc", "fn", "func", "cb", "evt", "ev", "addr",
        "pos", "len", "sz", "buf", "ret", "rslt", "qry", "db", "ent", "ctl", "ctrl", "rq", "rs", "ds", "mdl", "itm", "q", "r", "c", "d", "t",
        "u", "o", "s", "p", "m", "n", "a", "b", "f", "g", "h", "l", "v", "w", "z", "dlg", "frm", "lbl", "txt", "img", "pic", "tbl", "grd",
        "sel", "opt", "opts", "params", "args", "attr", "attrs", "prop", "el2", "data2", "res2", "temp2", "arr2",
    };

    public static readonly HashSet<string> ShortNameExceptions = new(StringComparer.Ordinal)
    {
        "id", "i", "j", "k", "ok", "_", "x", "y", "z", "e", "$", "t", "a", "b", "fn", "cb", "el",
    };

    public static readonly HashSet<string> GenericNames = new(StringComparer.Ordinal)
    {
        "result", "results", "response", "data", "item", "items", "value", "values", "payload", "output", "input", "content", "options",
        "settings", "existing", "config", "params", "body", "entries", "entry", "newState", "nextState", "updated",
    };

    public static readonly string[] GenericPrefixes = ["existing", "normalized", "updated", "trimmed", "filtered", "sorted", "mapped", "formatted", "parsed", "validated", "sanitized"];

    [GeneratedRegex(@"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+")]
    public static partial Regex CamelPart();

    [GeneratedRegex(@"[A-Za-z_$][A-Za-z0-9_$]*")]
    public static partial Regex Identifier();

    [GeneratedRegex(@"[\p{L}][\p{L}'-]*")]
    public static partial Regex Word();

    [GeneratedRegex(@"[А-Яа-яЁё]")]
    public static partial Regex Cyrillic();

    [GeneratedRegex(@"[A-Za-z]{3,}")]
    public static partial Regex Latin();

    [GeneratedRegex(@"\b(TODO|FIXME|HACK|XXX|КОСТЫЛЬ|ToDo|todo|fixme)\b")]
    public static partial Regex Todo();

    [GeneratedRegex(@"\b[A-Z][A-Z0-9]{1,9}-\d{1,6}\b|(?<![\w&])#\d{2,6}\b|\b(task|задача|тикет|issue|bug)\s*#?\d+", RegexOptions.IgnoreCase)]
    public static partial Regex Ticket();

    [GeneratedRegex(@"^[А-ЯЁа-яё][а-яё]{2,}(ем|ём|им|ам|ям)$")]
    public static partial Regex RussianNarrative();

    [GeneratedRegex(@"(костыл|дерга|дёрга|хз|пока так|временно|потом|переделать|кривов|херн|фигн|почему-то|зачем-то|!!|\?\?|\)\)|\bвроде\b|\bщас\b|\bнадо\b)", RegexOptions.IgnoreCase)]
    public static partial Regex Slang();

    [GeneratedRegex(@"^(Arrange|Act|Assert|Act\s*&\s*Assert|Given|When|Then|Setup)\b[\s:.]*$", RegexOptions.IgnoreCase)]
    public static partial Regex AaaMarker();

    [GeneratedRegex(@"^(Updated?|Changed?|Modified|Fixed|New|Added|Removed|Refactored)\b.*\b(to|from|instead|now)\b", RegexOptions.IgnoreCase)]
    public static partial Regex ChangeNote();

    /// <summary>Комментарий похож на закомментированный JS/TS/разметку.</summary>
    [GeneratedRegex(@"^(const|let|var|return|import|export|await|if\s*\(|for\s*\(|while\s*\(|switch\s*\(|function\b|this\.|console\.|<\/?[A-Za-z][\w.-]*[\s>/]|[\w$.\]\)]+\s*(=|\+=|-=|===?|!==?)\s*[\w$'""`\[{(]|[\w$.]+\([^()]*\)\s*;?$|\}\s*(else|catch|finally)?\s*\{?$|[)\]}];?$)")]
    public static partial Regex CodeLike();

    /// <summary>Заголовок первой фразы JSDoc: «Returns …», «Fetches …», «Custom hook that …».</summary>
    [GeneratedRegex(@"^(Returns|Gets|Sets|Fetches|Renders|Handles|Creates|Checks|Validates|Formats|Converts|Calculates|Computes|Updates|Loads|Saves|Builds|Generates|Determines|Resolves|Parses|Maps|Filters|Sorts|Displays|Shows|Hides|Toggles|Initializes|Resets|Clears|Removes|Adds|Provides|Represents|Manages|Wraps|Custom hook|React hook|Hook that|Hook for|Component that|Component for|Component to|Utility|Helper|Service (that|for)|Props for|Configuration for|Options for|Type for|Interface for|Represents)\b")]
    public static partial Regex DocTemplate();

    [GeneratedRegex(@"\b(Failed to|Something went wrong|An error occurred|An unexpected error|Unknown error|Network response was not ok|Unexpected error|Error fetching|Error loading|Error saving|Error creating|Error updating|Error deleting|Не удалось|Произошла ошибка|Что-то пошло не так|Ошибка (при|загрузки|сохранения))", RegexOptions.IgnoreCase)]
    public static partial Regex FailedMessage();

    [GeneratedRegex(@"HTTP error!? status|HTTP \$\{|status:\s*\$\{", RegexOptions.IgnoreCase)]
    public static partial Regex HttpErrorPhrase();

    [GeneratedRegex(@"^[A-ZА-ЯЁ][^:]{3,80}:\s*$")]
    public static partial Regex LogPrefix();

    [GeneratedRegex(@"test@example\.com|user@example\.com|example\.com|John Doe|Jane (Doe|Smith)|Test User|Alice|Bob\b|password123|'user-1'|""user-1""|Lorem ipsum|Иван(ов)?\b|Пётр|Петров|Тестовый пользователь", RegexOptions.IgnoreCase)]
    public static partial Regex ExampleData();

    public static bool IsCommentedCode(string body)
    {
        var b = body.Trim();
        if (b.Length < 3)
        {
            return false;
        }

        if (b.EndsWith(';') && (b.Contains('(') || b.Contains('=')))
        {
            return true;
        }

        return CodeLike().IsMatch(b) && b.Count(char.IsLetter) < b.Length * 0.9 || b.StartsWith('<') && b.EndsWith('>');
    }

    public static bool HasTranslit(string identifier)
    {
        foreach (Match part in CamelPart().Matches(identifier))
        {
            var p = part.Value.ToLowerInvariant();
            if (p.Length >= 4 && TranslitRoots.Contains(p))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasTypo(string text)
    {
        foreach (Match m in Identifier().Matches(text))
        {
            foreach (Match part in CamelPart().Matches(m.Value))
            {
                if (part.Value.Length >= 4 && Misspellings.Contains(part.Value.ToLowerInvariant()))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static HashSet<string> SplitIdentifiers(string code)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Identifier().Matches(code))
        {
            foreach (Match part in CamelPart().Matches(m.Value))
            {
                if (part.Value.Length >= 2)
                {
                    result.Add(part.Value.ToLowerInvariant());
                }
            }
        }

        return result;
    }

    public static bool IsPascal(string name) => name.Length > 1 && char.IsUpper(name[0]) && name.Any(char.IsLower);

    public static bool IsHookName(string name) => name.Length > 3 && name.StartsWith("use", StringComparison.Ordinal) && char.IsUpper(name[3]);

    public static bool IsScreaming(string name) => name.Length >= 3 && name.All(c => char.IsUpper(c) || char.IsDigit(c) || c == '_') && name.Any(char.IsLetter);
}
