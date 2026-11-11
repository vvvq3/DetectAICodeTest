# Для разработчиков: что где лежит

PDF-версия для передачи коллегам — [HANDOVER.pdf](HANDOVER.pdf).

Детектор оценивает, написал ли мердж-реквест человек или Claude Code. Сейчас он работает с двумя языками: C# и фронтендом (JavaScript/TypeScript, React, Angular, Vue). Код анализируется только локально, никуда не отправляется.

## Начать

```bash
git clone https://github.com/vvvq3/DetectAICodeTest.git
cd DetectAICodeTest
bash run.sh web            # веб-версия на http://localhost:5057 (сама поставит .NET 10 и соберёт проект)
```

Windows: `run.cmd web`. Публичная ссылка с паролем для коллег: `bash share.sh git.dextechnology.com`.

Сборка и тесты:

```bash
dotnet build src/AiDetect.Cli -c Release -o .build
dotnet test tests/AiDetect.Tests            # C# и ядро
dotnet test tests/AiDetect.Frontend.Tests   # фронтенд
```

## Структура

| Папка | Что там |
|---|---|
| `src/AiDetect.Core` | Ядро, общее для всех языков: загрузка MR (`Sources/` — GitLab, локальный git, JSON), диффы, сигналы процесса и формы MR (`Signals/`), модель и итоговый процент (`Analysis/`), калибровка и оценка (`Evaluation/`), отчёты HTML и консоль (`Reporting/`), интерфейс языка `Languages/ILanguageAnalyzer.cs` |
| `src/AiDetect.Languages.CSharp` | Признаки C# на Roslyn: экстрактор, сигналы, классификатор файлов, конвенции проекта, стек из `.csproj` |
| `src/AiDetect.Languages.Frontend` | То же для JS/TS. Свой мини-парсер в `Parsing/`: `JsLexer` (JS/TS/JSX/TSX), `Markup` (Vue SFC, HTML-шаблоны Angular, CSS). Стек — из `package.json` |
| `src/AiDetect.Cli` | Программа `aidetect`: команды и веб-сервер (`WebServer.cs`). Список языков — `AppLanguages.cs` |
| `model/weights.<язык>.json` | Веса модели, полученные калибровкой на корпусе |
| `corpus/<язык>/` | Манифесты и патчи корпуса для оценки. Сами MR в git не хранятся: восстанавливаются командой `python3 scripts/rebuild_corpus.py --lang csharp\|frontend` |
| `scripts/` | Сборка корпуса, оценка (`evaluate.sh csharp\|frontend`), примеры отчётов, `rundetectai.sh` — запуск на Маке одной командой |
| `tests/` | Модульные тесты (xUnit) и проверка веб-страницы Playwright (`tests/web`) |
| `docs/` | Документация (ниже) |

## Документы

- `README.md` — запуск, работа с GitLab, качество, настройка под свою команду.
- `docs/CONTEXT.md` — полная история проекта: требования, архитектура, решения, подводные камни. **Начинать с него.**
- `docs/RESEARCH.md`, `docs/frontend/RESEARCH.md` — исследования признаков ИИ-кода для C# и фронтенда.
- `docs/EVALUATION.md`, `docs/frontend/EVALUATION.md` — корпус и метрики качества.
- `CHANGELOG.md` — версии.
- `CLAUDE.md` — правила для Claude Code в этом репозитории.

## Как продолжать

- **Ветки.** Основная линия — `claude/inspiring-lamport-i2ixa7` (ветка по умолчанию). Новую работу вести в своей ветке и вливать в основную. Ветки `v1` (только C#) и `v2` (C# и фронтенд) и теги `v1.0.0`, `v2.0.0` — точки отката, их не менять.
- **Новый язык** — отдельный проект `src/AiDetect.Languages.<Язык>` с классом, который реализует `ILanguageAnalyzer`, и одна строка в `src/AiDetect.Cli/AppLanguages.cs`. Затем собрать корпус в `corpus/<язык>/`, откалибровать веса `model/weights.<язык>.json` и добавить тесты. Пошаговый рецепт — `docs/CONTEXT.md`, раздел 10.
- **Регрессия.** Изменения в ядре не должны менять результат существующих языков: прогнать `scripts/evaluate.sh csharp` и `scripts/evaluate.sh frontend` до и после изменения.
- **Точность на своём коде.** Разметить свои MR (человек / ИИ) и перекалибровать веса — README, раздел «Настройка под вашу команду».
