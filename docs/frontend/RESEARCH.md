# Исследование: код Claude Code или человека во фронтенд-MR (JS/TS, React, Angular, Vue, CSS)

> **Статус.** Обзор литературы и каталог гипотез подготовлены до реализации. Что подтвердилось на корпусе (306 MR) — в разделе 6 в конце и в [EVALUATION.md](EVALUATION.md). Реализация — `src/AiDetect.Languages.Frontend`; идентификаторы признаков в коде совпадают с каталогом, где это возможно.

Черновик для `docs/frontend/RESEARCH.md`. Модель та же, что в C#: каждый признак — доля или плотность в добавленном коде MR, затем свидетельство `e ∈ [−1, 1]`, взвешенная сумма и сигмоида. Есть поправки на базовую линию проекта и на конвенции (линтеры). Всё ниже — **гипотезы**: направление и силу признаков нужно подтвердить на корпусе. Литературные данные отмечены ссылками [n], остальное — экспертная оценка.

## 0. Главное коротко

1. **Нейросетевые детекторы плохо переносятся.** На «своих» данных они дают F1 0.8–0.99. На новых языках, доменах и генераторах качество резко падает: на SemEval-2026 Task 13 у UniXcoder macro-F1 упал с 0.99 до 0.85 [11]; см. также [5][10]. Интерпретируемые признаки (доля комментариев, глаголы в комментариях) переносятся лучше [12]. Наш подход верен, но решает калибровка на своём домене.
2. **Публичных измерений именно по фронтенду мало.** По JS есть атрибуция модели на Node.js-бэкенде: 86–95% даже после минификации [8]. Это значит, что «структурный отпечаток» (поток данных, структура) переживает поверхностную маскировку. Специальных работ по TS/React/Angular/Vue нет. Ближе всего — исследования агентных PR (AIDev), где TS/JS составляют большую долю [15–19].
3. **Claude Code — не «чат-LLM».** Его системный промпт 2025–2026 годов прямо запрещает классические отпечатки LLM [22]: «по умолчанию не писать комментарии» и не объяснять, ЧТО делает код; не добавлять docstring и типы к неизменённому коду; не обрабатывать ошибки в невозможных сценариях; не вводить абстракции ради одного случая и прослойки обратной совместимости. Поэтому повествовательные комментарии и JSDoc у Claude Code слабее, чем у ChatGPT или Copilot. Ставка — на корректность (отмена и отписка, `response.ok`, сужение `unknown`), тесты (сильные утверждения [19], Testing Library), форму MR (многоцелевые MR, «комплект» файлов) [14], процесс и артефакты (общие с C#).
4. **Свойства агентных PR Claude Code** [14]: чаще рефакторинг (24.9% против 14.9%), документация (22.1% против 14.0%) и тесты (18.8% против 4.5%); многоцелевых PR 39.9% против 12.2%; описание — медиана 355 слов против 56; добавленных строк — медиана 48 против 24. В TS у Claude Code больше всех «продвинутых» типовых конструкций: 6.74 на PR против 2.66 у людей. Агенты в среднем добавляют `any` в 9 раз чаще людей [16], так что «`any` = человек» — не аксиома.
5. **Временной перекос — главный источник ложных срабатываний во фронтенде.** API меняются быстрее, чем в .NET: сигналы и `@if` в Angular, `<script setup>` в Vue, React 19 и React Compiler, `satisfies`, `useUnknownInCatchVariables`. Сами фреймворки публикуют правила для LLM: angular.dev/ai требует `inject()`, сигналы, `input()` и `@if` [23]. «Современность» считаем только относительно версий из `package.json` и кода проекта.
6. **Конвенции гасят половину синтаксических признаков.** Это Prettier, ESLint (typescript-eslint, airbnb, angular-eslint, vue, jsx-a11y, testing-library), stylelint и `tsconfig strict`. Нужна таблица «правило → признаки», раздел 3.

## 1. Литература и практика 2023–2026

### 1.1 Детекторы и бенчмарки

| Работа | Что нашли | Что применимо у нас |
|---|---|---|
| **GPTSniffer** (Nguyen и др., JSS 2024) [1] | Дообученный CodeBERT отличает код ChatGPT (Java) лучше, чем GPTZero и OpenAI Classifier; помогают парные примеры и похожая обучающая выборка | Общие текстовые детекторы на коде бесполезны; полезны парные примеры (та же задача, человек и ИИ) — наш корпус «те же репозитории» |
| **How Far Are We?** (Suh и др., ICSE 2025) [2] | Детекторы ИИ-текста на коде дают около 50%; лучший вариант — эмбеддинги по AST и дообучение LLM (F1 82.6); абляция признаков | Подтверждает признаки на уровне структуры (AST), а не текста; потолок без своего корпуса невысок |
| **DetectCodeGPT / Between Lines of Code** (Shi и др., ICSE 2025) [3] | Код LLM лаконичнее, с более узким словарём токенов, «конвенционален»; сильнее всего отличаются стилистические токены (пробелы, переводы строк) и синтаксическая сегментация | Регулярность форматирования без Prettier (`fmt.*`), бедность словаря имён. С Prettier форматный признак исчезает |
| **CodeGPTSensor** (2024, TOSEM) [4] | UniXcoder с контрастивным обучением, 550 тыс. пар Python/Java. Различают многословность имён и структурная сложность. Люди угадывают на уровне 50% | `name.length` и `name.generic` (в C# длина имён оказалась слабой), сложность функций |
| **CoDet-M4** (Orel и др., Findings ACL 2025) [5] | 500 тыс. образцов, много языков и генераторов. Модели с AST лучше классических признаков, но падают на незнакомых доменах и языках | Калибровать отдельно на JS/TS; не переносить веса C# |
| **AIGCodeSet** (Demirok, Kutlu, 2024) [6] | Python, CodeNet, 3 модели. Лучший — байесовский классификатор. Сложнее всего случаи, когда ИИ правил человеческий код | В MR, где Claude правит существующие файлы, сигналы слабее: опора на новые файлы и базовую линию |
| **Is This You, LLM?** (Gurioli и др., SANER 2025) [7] | Один многоязычный классификатор на 10 языков, точность около 84%. Для JavaScript отдельная модель около 90%, общая — около 79% (по таблице, проверить) | Отдельная модель под язык лучше общей: веса `weights.frontend.json` |
| **Hidden DNA of LLM-Generated JavaScript** (2025) [8] | 50 тыс. Node.js-программ от 20 LLM (Claude среди них нет). Атрибуция модели 86–96%, после минификации и манглинга около 84–90%. Главные признаки — поток данных и структура, а не имена | Структурные признаки устойчивы к переименованию и маскировке. Генерированный JS — ES-модули вместо `require` |
| **Case study Claude 3 Haiku** (Rahman и др., 2024) [9] | 22 метрики. Функции длиннее за счёт комментариев и пустых строк, классы короче; точность на уровне функций 82%, классов 66% | Плотность комментариев и пустых строк. Но это чат-модель, не Claude Code |
| **DROID** (Orel и др., EMNLP 2025) [10] | 7 языков, 43 генератора, смешанное авторство и «очеловеченный» (DPO) код. Готовые детекторы на нём рушатся: GPTZero — полнота около 0.10 | Маскировку надо моделировать в корпусе (`ai-masked`). Устойчивы только признаки корректности, формы MR и процесса |
| **SemEval-2026 Task 13** [11][12] | Бинарная и многоклассовая детекция, гибриды, сильный сдвиг распределения (JS не встречается в обучении). UniXcoder: 0.99 на валидации → 0.85 на тесте. Дерево глубины 2 на долях комментариев и глаголов в них — macro-F1 67, топ-15% | Подтверждает `cmt.narrative` (глагольные комментарии) и доли, нормированные на длину |
| **Code rewriting** (Ye и др., AAAI 2025) [13] | Детекция без обучения: LLM переписывает код, и свой код она меняет меньше, чем чужой | Требует LLM — у нас запрещено (только локально, без ИИ-сервисов). Не используем |
| **Human vs AI code: дефекты и сложность** (Cotroneo и др., ISSRE 2025) [20] | Код ИИ проще и однообразнее, но в нём чаще неиспользуемые конструкции и отладочные артефакты (Python/Java, 500 тыс. образцов) | `mth.complexity` ниже у ИИ; «неиспользуемые конструкции» у Claude Code проверить, его промпт велит удалять неиспользуемое |
| **Монокультура** (Burtch 2026; Wu и др. ICLR 2025) [21] | Синтаксическая гомогенизация кода людей под влиянием LLM (seed 42 и т. п.) при слабой семантической | «Человеческий» класс 2025–2026 годов уже частично похож на ИИ: дикую выборку оценивать отдельно |

### 1.2 Агентные PR (Claude Code и другие агенты)

| Работа | Что нашли | Что применимо |
|---|---|---|
| **Watanabe и др., TOSEM 2025/26** [14] | 567 PR Claude Code (поиск по «Generated with Claude Code») против 567 человеческих. Метрики приведены в п. 4 раздела 0. Принято 83.8% против 91.0%; правки ревьюеров — fix 47.7%, docs 29%, style 23.4% | `mr.multiPurpose`, `proc.description`, `mr.testsWithCode`, `mr.docs`; «style»-правки — сигнал несоответствия стилю проекта |
| **AIDev** (Li, Zhang, Hassan 2025; MSR’26) [15] | 456 тыс. → 932 тыс. агентных PR (Codex, Copilot, Devin, Cursor, Claude Code), подмножество AIDev-pop — 33.6 тыс. PR | Готовый источник «дикой» выборки JS/TS PR Claude Code с диффами (Hugging Face `hao-li/AIDev`) |
| **Mining Type Constructs** (Lee, Ul Hassan, Hindle, MSR’26) [16] | TS: агенты добавляют `any` около 2.16 раза на PR против 0.24 у людей. «Продвинутых» типов: Claude Code 6.74 на PR против 2.66 у людей (d = 1.45). Ложные срабатывания регулярок: `type="button"`, MIME-типы | `ts.utility`, `ts.typeGuard`. Направление `ts.any` проверять на корпусе. Лексер должен отличать атрибут `type=` от ключевого слова `type` |
| **Tests in Agentic PRs** (Haque и др., MSR’26) [17] | Доля PR с тестами растёт; у Claude к июлю 2025 года — 55% | `mr.testsWithCode` — ожидаемо сильный |
| **Human-Agent vs Human PRs** (Milanese и др., 2026) [18] | Java/JS/Python/TS: тесты в 42.9% против 40.0% PR; отношение тест/код 0.33 против 0.19; тестовые «запахи» почти не различаются | `mr.testRatio` сильнее, чем просто наличие тестов |
| **All Smoke, No Alarm** (Banik и др., 2026) [19] | 80% агентных тест-патчей со слабыми оракулами. У Claude Code сильные оракулы — 67% в новых тест-файлах (Codex — 18%) | `test.oracles`: сильные утверждения (`toEqual`, `toHaveBeenCalledWith`) за Claude, `toBeTruthy` и снапшоты — против |
| **Доступность веб-кода LLM** (Suh и др., 2025) [25] | Код GPT-4o и Qwen доступнее человеческого в базовом (alt, контраст), но хуже в ARIA | `a11y.altText`, `a11y.labelFor` — за ИИ; направление `a11y.ariaDensity` — проверять |
| **Безопасность кода Copilot** (Fu и др., TOSEM 2025) [26] | 24% JS-фрагментов Copilot в GitHub-проектах со слабостями CWE | Для нас вторично (это не Claude и не стиль) |

### 1.3 Практика разработчиков (неформальные источники)

- **Системный промпт Claude Code** (реконструкции 2025–2026) [22]: «Default to writing no comments… Don't explain WHAT the code does»; «Don't add error handling, fallbacks, or validation for scenarios that can't happen… Only validate at system boundaries»; «Don't create helpers… for one-time operations»; «Avoid backwards-compatibility hacks like renaming unused _vars, re-exporting types»; «Don't reference the current task, fix, or callers»; «verify it actually works: run the test». Ранние версии (2025): «IMPORTANT: DO NOT ADD ***ANY*** COMMENTS unless asked». Отсюда: комментарии редкие и объясняют ПОЧЕМУ, ссылок на задачу в коде нет, валидация — на границах (fetch, формы), тесты прогоняются, MR «готов с первого раза».
- **Навыки deslop / rmslop** для Claude Code и Cursor [27] вычищают как «ИИ-мусор»: лишние комментарии, нехарактерные для файла; лишние защитные проверки и try/catch на доверенных путях; касты к `any` ради обхода ошибок типов; стиль, отличный от остального файла; эмодзи. Это прямой список признаков; ключевое — «несоответствие окружающему файлу», то есть наш `shift.style`.
- **Чек-лист «Detecting AI code»** [28]: уровень 1 — трейлеры, футеры, `CLAUDE.md`; уровень 2 — пересказывающие и «туториальные» комментарии, JSDoc на 20 строк к функции из 5 строк; уровень 3 — защитные проверки «невозможного», одинаковые шаблоны во всех файлах. За человека — TODO/FIXME, `console.log`, закомментированный код, сокращения (`amt`, `curr`), внутренний жаргон, ссылки на Stack Overflow.
- **Блоги о React** [29] («Thank you for explaining setLoading(false) to me»): комментарий на каждой строке, тройка `loading / error / data`, `finally { setLoading(false) }`.
- **«AI slop» в вёрстке** [30]: индиго- и фиолетовые градиенты, шрифт Inter, «три карточки с тенью», палитра Tailwind по умолчанию (`#6366f1`), центрированный hero. Сигнал для `css.llmValues`, но время и Tailwind дают ложные срабатывания.
- **Angular** [23] публикует best-practices для LLM: standalone, сигналы, `inject()`, `input()`/`output()`, `@if`/`@for`, без `ngClass`/`ngStyle` и `@HostBinding`/`@HostListener`, `NgOptimizedImage`; в v20+ — «не ставить `standalone: true` и OnPush явно». ИИ-код будет «по последней документации», люди — по версии проекта.
- **Vue** [31]: типичные ошибки чат-LLM — Options API в Vue 3, `defineProps(['a'])` без типов, деструктуризация реактивного объекта, ручной `modelValue` вместо `defineModel`. Claude Code обычно повторяет стиль соседних SFC.

### 1.4 Выводы для нашего признакового подхода

- Лучшие независимые группы: (1) корректность — отмена и отписка, `response.ok`; (2) шаблоны ошибок и логов — `console.error('Failed to …:', error)`; (3) тесты — Testing Library, сильные утверждения, `it.each`; (4) форма MR и процесс; (5) отсутствие «человеческого мусора» — `console.log`, `debugger`, TODO, закомментированный код, `@ts-ignore`.
- Комментарии и JSDoc держать, но не делать опорой: у Claude Code они подавлены промптом, а при маскировке («пиши как в проекте») исчезают первыми.
- Все «модные» API — только как сдвиг относительно проекта и версии, с маленьким весом.

## 2. Каталог признаков

**Обозначения.** **→** — направление: И = за Claude, Ч = за человека. **Сила**: В (высокая), С (средняя), Н (низкая). **Маск.** — устойчивость к маскировке «пиши как в проекте»: У (устойчив), Ч (частично), Л (легко убирается). **LOC** — непустые некомментарные добавленные строки JS/TS; для разметки и стилей — отдельные LOC шаблонов и CSS. Все счётчики — только по добавленным строкам MR (новый файл — целиком), с примерами `файл:строка`.

**Что даёт лексер и мини-парсер.** Токены: идентификатор, ключевое слово, пунктуация, строка, шаблонная строка с частями, regex, число. Комментарии `//`, `/* */` и отдельно JSDoc `/** */`. JSX: элемент, атрибут, `{/* */}`, текст. Декораторы. Объявления: функция, стрелочная функция, метод, класс, interface, type, enum, импорт и экспорт; границы тел и вложенность. «Компонент» — функция с именем в PascalCase, возвращающая JSX, класс с `@Component` или Vue SFC; «хук» — `use[A-Z]`. Вызовы `X(` с именем вызываемого. Для HTML-шаблонов — элементы и атрибуты; для CSS — правила и декларации (свойство, значение).

### 2.1 Документация (JSDoc/TSDoc)

| id | Признак: что считать / знаменатель | → | Сила | Маск. | Риск ложного срабатывания |
|---|---|---|---|---|---|
| doc.coverage | Экспортируемые объявления (`export function/const/class`, public-методы сервисов) с `/** */` перед ними (между ними только декораторы) / все экспортируемые объявления | И (у Claude Code слабее) | С→Н | Л | Библиотеки и дизайн-системы, `jsdoc/require-jsdoc`, typedoc/compodoc в devDeps |
| doc.template | JSDoc, первая фраза которого — глагол в 3-м лице или шаблон (`Returns…`, `Fetches…`, `Renders…`, `Custom hook that/for…`, `Component that…`, `Utility function to…`), с точкой в конце / все JSDoc | И | С | Л | Низкий |
| doc.tsdocParams | `@param name - описание` и `@returns` без `{Type}` в .ts — И; `@param {string} name` в .ts — Ч (привычка из JS) / теги `@param` | И / Ч | С | Л | JS-проекты с `checkJs` |
| doc.propsMembers | Члены `interface …Props` и опций с однострочным `/** … */` / члены таких интерфейсов | И | С | Л | Библиотеки компонентов (Storybook autodocs) — сравнивать с базой |
| doc.extraTags | `@example`, `@throws`, `@default`, `@see` в прикладном коде / JSDoc | И | Н–С (редко) | Л | Библиотеки |
| doc.fileHeader | Первая строка файла — путь или имя файла (`// src/hooks/useAuth.ts`), `@fileoverview`, «This file contains…» | И | В, если есть (редко у Claude Code) | Л | Лицензионные шапки — исключать |
| doc.overDoc | Строки JSDoc / строки тела функции > 1 | И | С | Л | Публичные API |

### 2.2 Комментарии

| id | Признак / знаменатель | → | Сила | Маск. | Риск |
|---|---|---|---|---|---|
| cmt.density | Строки `//` и `/* */` без JSDoc, директив `eslint-`/`@ts-`/`prettier-` и лицензий, на 100 LOC | ? (чат-Claude ↑, Claude Code ↓) | Н | Л | Только как контекст и сдвиг |
| cmt.narrative | Комментарии «Заглавный глагол + 1–5 слов, без точки»: `// Fetch users`, `// Handle form submission`, `// Reset form`, `// Clean up`. Словарь около 60 глаголов (Handle, Fetch, Load, Update, Reset, Clear, Check, Validate, Initialize, Toggle, Calculate, Format, Parse, Filter, Sort, Render, Show, Hide, Ensure, Prevent, Sync, Cleanup…). Русские («Получаем», «Проверяем») — только с заглавной / все комментарии | И | С–В | Л | Аккуратные люди. Подтверждено [12] (verb-comment ratio) |
| cmt.restating | Слова комментария (camelCase разбит, стемминг) пересекаются с идентификаторами следующей строки на ≥ 60% / комментарии над кодом | И | С | Л | Низкий |
| cmt.jsxSection | `{/* Header */}`, `{/* Loading state */}`, `{/* Actions */}` — 1–4 слова с заглавной перед JSX-элементом; то же `<!-- Header -->`, `<!-- Empty state -->` в шаблонах Angular/Vue / JSX-элементы и элементы шаблона на 100 строк разметки | И | В | Л | Вёрстка по макету с секциями |
| cmt.cssSection | `/* Header styles */`, `/* ===== Buttons ===== */`, `/* Responsive */`, `/* Dark mode */` / CSS-правила | И | С | Л | Большие таблицы стилей людей — по базе |
| cmt.why | Комментарии с причинной связкой (because, since, otherwise, to avoid, Note:, workaround, «чтобы», «иначе») / все комментарии | И (слабо, Claude Code) | Н | Л | Нейтрален до замера |
| cmt.taskRef | Тикеты и задачи (`JIRA-123`, `#123`, `@ivanov`, даты, «временно», «по просьбе») — Ч; отчёт о правке (`// Updated to…`, `// Changed from X to Y`, `// New:`) — И (чат) | Ч / И | С | Ч | Низкий |
| cmt.casual | Со строчной буквы, `//без пробела`, `???`, `!!!`, сленг, `:)`, транслит | Ч | С | Ч | Prettier не трогает текст комментариев — признак работает |
| cmt.language | Язык комментариев относительно проекта (как в C#) | Ч / И | С | Ч | Международные команды |
| cmt.emoji | Эмодзи в комментариях и строках (✅ ❌ 🚀 ⚠️) | И | С (редко) | Л | UI-тексты с эмодзи — исключать JSX-текст и i18n |

### 2.3 Именование

| id | Признак / знаменатель | → | Сила | Маск. | Риск |
|---|---|---|---|---|---|
| name.catchParam | Параметр `catch (x)` и `.catch((x) =>`: `error` — И; `e`, `err`, `ex` — Ч / все catch | И / Ч | В–С | Ч | Команды с конвенцией `error`; `naming-convention` |
| name.lambdaParam | В `.map/.filter/.find/.some/.every/.forEach/.reduce` первый параметр — единственное число коллекции (`users.map((user) =>`) — И; одна буква (`x`, `i`, `el`, `o`) — Ч / такие колбэки | И / Ч | С | Ч | Низкий |
| name.abbrev | Сокращения из словаря (res, resp, req, btn, idx, val, tmp, cb, el, arr, obj, str, num, cnt, msg, cfg, ctx; `prev` и `e` — исключить) / локальные имена и параметры | Ч | С | Ч | `res/req` в Express-коде — норма; по базе |
| name.handler | JSX-атрибуты `on[A-Z]\w*={ident}`, где ident = `handle[A-Z]…` / все обработчики-идентификаторы | И | С | Ч | Конвенция из документации React — только со сдвигом |
| name.boolState | Булевы `useState/ref/signal` (инициализатор `true/false`) с префиксом is/has/should/can / все булевы | И | С–Н | Ч | Конвенции |
| name.propsType | Компоненты с типом пропсов `${Component}Props` / компоненты с типизированными пропсами | И | С | Ч | `Props`/`IProps` в проекте — Claude повторит |
| name.iPrefix | `interface I[A-Z]…` / интерфейсы | Ч | С (по базе) | Ч | Принято в проекте (C#-команды) |
| name.underscorePrivate | `private _foo`, `this._foo` / приватные поля | Ч | С | Ч | Старый Angular-стиль в проекте |
| name.constCase | Модульные `const SCREAMING_CASE = литерал` на 100 LOC; «магические» числа в выражениях — обратный счётчик | И | С | Ч | Конфиги |
| name.generic | `data, result, response, items, value` и шаблоны `filteredX, sortedX, updatedX, newX, existingX` / локальные имена | И | С | Ч | Низкий |
| name.translit | Транслит и кириллица в идентификаторах (как в C#) | Ч | В, если есть | Ч | — |
| name.length | Средняя длина локальных имён | И | Н (в C# слабый) | Ч | — |

### 2.4 Типизация TS (.ts, .tsx, `<script lang="ts">`)

Знаменатель «аннотации»: позиции типов (`:` в параметрах, переменных и полях; `as T`; дженерик-аргументы).

| id | Признак / знаменатель | → | Сила | Маск. | Риск |
|---|---|---|---|---|---|
| ts.any | `: any`, `as any`, `<any>`, `any[]`, `Record<string, any>` / аннотации. Раздельно: в тестах и моках против прикладного кода | ? (было Ч; по [16] агенты ×9) | С | Ч | `no-explicit-any`. Направление — только по корпусу |
| ts.unknown | `unknown` (catch, параметры, `Record<string, unknown>`) / аннотации | И | С | Ч | TS ≥ 4.4 strict |
| ts.doubleCast | `as unknown as X` | И | С (редко) | Ч | Тесты |
| ts.nonNull | Постфиксный `!` после ident, `)` или `]`, за которым `.`, `)`, `;`, `,` или `[` (не `!=`). `foo!: T` — отдельно, нейтрально / обращения `.` и `?.` | Ч | С | Ч | `no-non-null-assertion`, `strictPropertyInitialization` |
| ts.tsIgnore | `@ts-ignore`, `@ts-nocheck` — Ч; `@ts-expect-error <описание>` — слабо И | Ч | С | Ч | `ban-ts-comment` |
| ts.literalUnion | `type X = 'a' \| 'b'` (строковые литералы) против `enum` / оба | И / Ч | С | Ч | Конвенция; `erasableSyntaxOnly` (TS 5.8) запрещает enum |
| ts.asConst | `as const` на 100 LOC | И | С | Л | — |
| ts.utility | `Record, Partial, Pick, Omit, ReturnType, Parameters, Awaited, NonNullable, Readonly` в дженериках / аннотации | И | С | Ч | Подтверждено [16] |
| ts.typeGuard | Предикаты `): x is T`, `never`-проверки (`assertNever`, `const _x: never`) | И | С (редко) | Ч | — |
| ts.returnTypes | Явный возвращаемый тип у экспортных не-компонентов (`Promise<User[]>`) / такие функции | И (слабо) | Н | Л | `explicit-function-return-type`; Angular CLI (`ngOnInit(): void`) |
| ts.genericState | Типизированные `useState<User \| null>(null)`, `useRef<HTMLInputElement>(null)`, `ref<T>()`, `signal<T[]>([])` / все такие вызовы с `null` или `[]` | И | С | Ч | `strict` вынуждает |
| ts.readonly | `readonly` в интерфейсах и полях, `readonly T[]` / члены | И | Н–С | Л | — |
| ts.importType | `import type` / `import { type X }` / импорты типов | И | Н | Л | `consistent-type-imports`, `verbatimModuleSyntax`, `isolatedModules` |

### 2.5 Современный синтаксис

| id | Признак / знаменатель | → | Сила | Маск. | Риск |
|---|---|---|---|---|---|
| syn.nullish | `??` / (`??` + `\|\|`, справа литерал, `[]` или `{}`) | И | С | Ч | `prefer-nullish-coalescing`; время (TS 3.7) |
| syn.optChain | `?.` против цепочек `a && a.b && a.b.c` / оба | И / Ч | С | Ч | `prefer-optional-chain` |
| syn.eqeq | `==` и `!=` (кроме `== null`) / сравнения | Ч | Н–С | Ч | `eqeqeq` — ×0 |
| syn.var | `var`; `let` без переприсваивания в области / объявления | Ч | С | Ч | `no-var`, `prefer-const` |
| syn.concat | `+` со строковым литералом против шаблонных строк / оба | Ч | С | Ч | `prefer-template` |
| syn.thenChains | `.then(` / (`.then(` + `await`), без RxJS и `Promise.all` | Ч | С | Ч | Старый код в файле |
| syn.radix | `parseInt(x)` без основания — Ч; `parseInt(x, 10)` или `Number(x)` — И | Ч / И | С (редко) | Ч | `radix` |
| syn.indexLoop | `for (let i = 0; i < a.length; i++)` против `for…of` и методов массивов | Ч | Н–С | Ч | Производительный код |
| syn.functionalUpdate | `setX(prev => …)` / все `setX(…)`, зависящие от текущего значения | И | С | У–Ч | Корректность — устойчиво |
| syn.optionalCall | `onChange?.(v)` / вызовы необязательных колбэков | И | С | Ч | — |
| syn.newApis | `structuredClone`, `crypto.randomUUID()`, `.at(-1)`, `Object.hasOwn`, `toSorted`, `findLast`, `??=` | И (только сдвиг) | С в сдвиге | Л | target/lib и browserslist; время |
| syn.destructProps | Деструктуризация пропсов в сигнатуре против `props.x` | И | Н | Л | — |

### 2.6 Защитный код и обработка ошибок

| id | Признак / знаменатель | → | Сила | Маск. | Риск |
|---|---|---|---|---|---|
| err.instanceofMessage | `error instanceof Error ? error.message : 'Unknown error'` или `String(error)` / catch-блоки, использующие сообщение | И | В | Ч | TS ≥ 4.4 strict вынуждает сужение. Человеческий вариант — `(e as Error).message`, `e.message` |
| err.responseOk | `fetch(`, за которым в ≤ 10 строк идёт `!res(ponse).ok` → throw. Отдельно — фраза `HTTP error! status:` / вызовы fetch | И | В | Ч–У | axios (там нет `ok`) — считать `validateStatus` и interceptors отдельно |
| err.failedMsg | Строки «Failed to …», «Something went wrong», «An error occurred», «Unknown error», «Network response was not ok» в throw, setError, toast или console / строковые литералы в этих позициях | И | С–В | Ч | i18n-ключи в проекте |
| err.loadingTriad | В одном компоненте или композабле `loading` + `error` + данные и `finally { setLoading(false) }` / компоненты с запросами | И | С | Ч | Состояние от React Query, RTK или NgRx — не считать |
| err.contextGuard | `createContext<T \| undefined>(undefined)` и `if (!ctx) throw new Error('useX must be used within XProvider')` | И | В (редко) | Ч | Паттерн из блогов 2020 года — по базе |
| err.tryDensity | try/catch на async-функцию в прикладном коде | И (слабо у Claude Code) | Н–С | Л | — |
| err.antipatterns | `throw 'строка'`, `throw {…}`, пустой `catch {}`, `catch (e) { console.log(e) }` / catch и throw | Ч | С | Ч | — |

### 2.7 Асинхронность, отмена и отписка (аналог CancellationToken — самая устойчивая группа)

| id | Признак / знаменатель | → | Сила | Маск. | Риск |
|---|---|---|---|---|---|
| async.effectCleanup | useEffect с ресурсом (addEventListener, setInterval, setTimeout, `subscribe(`, `observe(`, WebSocket, EventSource, *Observer, fetch) и `return () => {…}` с парной очисткой (remove…, clear…, unsubscribe, disconnect, close, abort) / эффекты с ресурсом | И | В | У | Низкий |
| async.abort | `new AbortController()` + `signal` в fetch/axios + `abort()` в cleanup; флаг `let ignore/cancelled = false` / эффекты с запросом. `isMounted` — старый человеческий паттерн | И | В | У | React Query, SWR, RTK Query — отмена встроена, гасить |
| async.abortErrorCheck | `error.name === 'AbortError'` | И | С (редко) | У | — |
| async.effectAsync | `useEffect(async () =>` | Ч | В (редко) | У | — |
| async.depsDisable | `eslint-disable-next-line react-hooks/exhaustive-deps` | Ч | С | Ч | — |
| async.ngTeardown | `.subscribe(` в компонентах и директивах, у класса которых нет teardown (`takeUntil(this.destroy$)`, `takeUntilDestroyed(`, `DestroyRef`, `Subscription` + `unsubscribe` в `ngOnDestroy`, `take(1)`/`first()`) — Ч / все subscribe в компонентах | И / Ч | В | У | HttpClient завершается сам — его subscribe учитывать отдельно |
| async.asyncPipe | Observable в шаблоне через `\| async` против subscribe с записью в поле | И | С | У–Ч | — |
| async.destroySubject | `destroy$ = new Subject<void>()` + `next()` и `complete()` в `ngOnDestroy` | И | С | Ч | — |
| async.vueCleanup | Ресурс в `setup` + `onUnmounted`/`onBeforeUnmount` с очисткой; `onCleanup` в `watchEffect` / setup с ресурсами | И | С–В | У | — |
| async.timerRef | Таймеры и дебаунсы в `useRef`, очищаемые в cleanup | И | С | У | — |
| async.promiseAll | `Promise.all`/`allSettled` против последовательных независимых await | И | Н | У | — |

### 2.8 Логирование

| id | Признак / знаменатель | → | Сила | Маск. | Риск |
|---|---|---|---|---|---|
| log.errorTemplate | `console.error/warn('<Предложение с заглавной>:', ident)` — `console.error('Failed to fetch users:', error)`, `'Error loading data:'` / вызовы console.error и warn | И | В | Ч | `no-console` (вызовов нет — признак молчит) |
| log.consoleLog | `console.log/debug/info` в прикладном не-тестовом коде на 100 LOC; особенно `console.log(x)`, `console.log('here')`, `console.log('data', data)` | Ч | В | Ч | Node-скрипты, CLI, SSR-сервер — по слою |
| log.logger | Проектный логгер с объектом контекста (`logger.error('…', { userId })`) | И (слабо) | Н | Ч | Только если логгер есть в базе |

### 2.9 React

| id | Признак / знаменатель | → | Сила | Маск. | Риск |
|---|---|---|---|---|---|
| react.memoDensity | `useCallback` + `useMemo` + `memo(` на компонент | И | С | Ч | React Compiler (2025+) — наоборот; норма проекта |
| react.customHooks | Новые `use[A-Z]…` в MR, файлы `hooks/use*.ts` / новые компоненты | И | С | Ч | — |
| react.keyIndex | `key={index\|i\|idx}` в `.map` / атрибуты `key` | Ч | С | Ч | `react/no-array-index-key` |
| react.fcType | `React.FC`/`FC<Props>` — Ч; `function Foo({…}: FooProps)` — И | Ч / И | Н–С | Ч | Время (CRA убрал FC в 2020) |
| react.legacy | Классовые компоненты, `PropTypes`, лишний `import React` при новом JSX-трансформе | Ч | только сдвиг | Ч | Чистое время |
| react.derivedStateEffect | useEffect, тело которого — только `setX(f(deps))` («You might not need an effect») | Ч | С | У | — |
| react.displayName | `Foo.displayName = 'Foo'` при forwardRef или memo | И | С | Л | shadcn/ui |
| react.states | В компоненте ветки `if (isLoading) return …; if (error) return …; if (!items.length) return <Empty/>` | И | С | Ч | — |
| react.inlineStyle | `style={{…}}` на 100 JSX-элементов | ? | Н | Ч | Замерить |

### 2.10 Angular (.ts с `@Component`/`@Injectable`, .html-шаблоны)

| id | Признак / знаменатель | → | Сила | Маск. | Риск |
|---|---|---|---|---|---|
| ng.ctorReadonly | Параметры конструктора с модификатором и `readonly` (`private readonly http: HttpClient`) / параметры с модификатором | И | С | Ч | Принято в проекте |
| ng.subscribeObserver | `.subscribe({ next, error })` против позиционных `subscribe(d => …, e => …)` (устарело в RxJS 7) | И / Ч | С | Ч | Время (RxJS 7, 2021) |
| ng.rxOperators | `catchError(() => of(…))`, `finalize(() => …loading = false)`, `debounceTime(300)` + `distinctUntilChanged()` + `switchMap` / pipe-цепочки | И | С | Ч | — |
| ng.typedHttp | `http.get<User[]>(` — И; `get<any>`, без дженерика или `map((res: any) =>` — Ч / вызовы HttpClient | И / Ч | С | Ч | — |
| ng.nestedSubscribe | `subscribe` внутри `subscribe` | Ч | С | У | — |
| ng.trackBy | `*ngFor` с `trackBy` / все `*ngFor`. В `@for` `track` обязателен — не считать | И | С | Ч | `use-track-by-function` |
| ng.onPush | `ChangeDetectionStrategy.OnPush` в новых компонентах | И (до v20) | С | Ч | `prefer-on-push…`; в новых версиях рекомендация обратная [23] |
| ng.modernApis | `inject(`, `signal/computed/effect`, `input()/output()/model()`, `@if/@for`, `takeUntilDestroyed` | И (только сдвиг) | Н | Л | Версия Angular и база; codemod-миграции |
| ng.typedForms | `fb.nonNullable.group`, `FormGroup<{…}>`, геттеры контролов | И (сдвиг) | Н–С | Ч | Версия ≥ 14 |
| ng.manualDom | `document.getElementById/querySelector`, `nativeElement.style` в компоненте | Ч | С | Ч | — |
| ng.lifecycleIfaces | `implements OnInit, OnDestroy` при наличии хуков | И (слабо) | Н | Л | CLI генерирует сам |

### 2.11 Vue (SFC: `<template>`, `<script>`, `<style>`)

| id | Признак / знаменатель | → | Сила | Маск. | Риск |
|---|---|---|---|---|---|
| vue.propsTyped | `defineProps<{…}>()`/`defineProps<Props>()` — И; `defineProps({ x: String })`, `defineProps(['a'])`, `props: [...]` — Ч / объявления props | И / Ч | С | Ч | `vue/define-props-declaration` |
| vue.withDefaults | `withDefaults(defineProps<Props>(), {…})` | И | С | Ч | — |
| vue.emitsTyped | `defineEmits<{…}>()` — И; `defineEmits(['x'])` или `$emit` без объявления — Ч | И / Ч | С | Ч | `vue/require-explicit-emits` |
| vue.optionsApi | `data()`, `methods:`, `this.$refs`, `this.$parent`, `$forceUpdate` | Ч (сдвиг) | С в сдвиге | Ч | Vue 2 и время |
| vue.vFor | `v-for` без `:key`, `:key="index"`, `v-if` + `v-for` на одном элементе | Ч | С | Ч | `vue/require-v-for-key`, `vue/no-use-v-if-with-v-for` |
| vue.composables | Новые `composables/useX.ts` | И | С | Ч | — |
| vue.modernApis | `<script setup lang="ts">`, `defineModel`, setup-сторы Pinia | И (только сдвиг) | Н | Л | Версии 3.2, 3.4 |

### 2.12 CSS/SCSS/LESS (а также `<style>` в SFC и `styles` в Angular)

| id | Признак / знаменатель | → | Сила | Маск. | Риск |
|---|---|---|---|---|---|
| css.vars | Цвета и отступы через `var(--…)`, особенно с фолбэком `var(--x, #hex)` / декларации цвета и отступов | И | С | Ч | Дизайн-токены проекта |
| css.llmValues | `box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1)` и подобные, `transition: … 0.2s ease`, `border-radius: 8px`, палитра Tailwind v2+ (`#3b82f6`, `#ef4444`, `#10b981`, `#6b7280`, `#e5e7eb`, `#6366f1`), системный стек `-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto` / декларации | И | С | Ч | Проекты на Tailwind — ×0; время (палитра с 11.2020) |
| css.focusVisible | `:focus-visible` — И; `outline: none/0` без замены — Ч | И / Ч | С | Ч | — |
| css.reducedMotion | `@media (prefers-reduced-motion` | И | С (редко) | Л | — |
| css.important | `!important` на 100 деклараций | Ч | С | Ч | `declaration-no-important` |
| css.zIndexHuge | `z-index ≥ 999` | Ч | С | Ч | — |
| css.nesting | Глубина вложенности SCSS > 4 | Ч | Н | Ч | `max-nesting-depth` |
| css.misc | `@media (max-width: 768px)`, `rem`, flex `gap`, ручные `-webkit-` | ± | Н | Л | Время; autoprefixer |
| css.twVariants | В Tailwind-проектах: доля интерактивных элементов с `focus-visible:ring-*`, `disabled:opacity-50 disabled:cursor-not-allowed`, `transition-colors` | И | С | Ч | Копии shadcn/ui |

### 2.13 Доступность (JSX, шаблоны Angular и Vue)

| id | Признак / знаменатель | → | Сила | Маск. | Риск |
|---|---|---|---|---|---|
| a11y.buttonType | `<button>` с явным `type=` / все `<button>`. Атрибут `type=` не путать с TS-ключевым словом [16] | И | В–С | Ч | `react/button-has-type` (airbnb), `@angular-eslint/template/button-has-type` |
| a11y.ariaLabelIcon | Кнопки и ссылки без текста (только иконка или `×`) с `aria-label` / такие элементы | И | С | Ч | — |
| a11y.ariaDensity | `aria-*` и `role` (особенно `aria-live`, `aria-busy`, `aria-invalid`, `aria-describedby`, `role="alert"`) на 100 элементов | И? | С | Ч | По [25] LLM хуже людей в ARIA — направление проверить; a11y-команды |
| a11y.labelFor | `<label htmlFor/for>` с парным `id` / поля ввода | И | С | Ч | — |
| a11y.altText | `<img>` с непустым alt / все img | И | С | Ч | `jsx-a11y/alt-text` |
| a11y.divClick | `onClick`/`(click)` на div или span без role, tabIndex и обработчика клавиатуры | Ч | С | Ч | jsx-a11y |

### 2.14 Ручные привычки (за человека)

| id | Признак / знаменатель | Сила | Маск. | Риск |
|---|---|---|---|---|
| habits.markers | Сводный, на 100 LOC: TODO, FIXME, HACK, XXX; `debugger;`; `alert(`; закомментированный код (`// const x =`, `// foo();`, `{/* <Old/> */}`, `<!-- <div` — эвристика LooksLikeCode из C#); `/* eslint-disable */` на файл и `eslint-disable-next-line`; `@ts-ignore`/`@ts-nocheck`; `// prettier-ignore`; `setTimeout(…, 0)`; `window.location.reload()`; jQuery `$(`; `document.getElementById` в компонентах | В | Ч | Claude оставит TODO, только если его попросить |
| habits.typos | Опечатки в идентификаторах, строках и комментариях (словарь, как в C#) | С | У | i18n-тексты не проверять |
| habits.unusedImports | Импортированное имя ни разу не используется в файле | С | Ч | TS `noUnusedLocals`, `unused-imports` — ×0 |
| habits.mixedStyle | Смешение `'` и `"`, есть `;` и нет `;` в одном файле (без Prettier) | С | Ч | Prettier — ×0 |
| habits.magicNumbers | Числа вне {0, 1, −1, 2, 100} в логике, не в объявлениях констант | Н–С | Ч | CSS-in-JS, конфиги |

### 2.15 Форматирование (только без Prettier, Biome или dprint)

| id | Признак | → | Сила | Маск. |
|---|---|---|---|---|
| fmt.irregular | Пробелы в конце строк, `if(`, `){`, `function(){`, `a=b`, двойные пустые строки, смешанные отступы, строки > 120 символов, нет перевода строки в конце файла | Ч | С | Ч |
| fmt.prettierLike | Доля строк «как после Prettier»: отступ 2, единый тип кавычек, висячие запятые, `(x) =>` | И | С | Ч |

### 2.16 Тесты (`*.test.*`, `*.spec.*`, `__tests__/`, e2e)

| id | Признак / знаменатель | → | Сила | Маск. | Риск |
|---|---|---|---|---|---|
| test.withCode / test.ratio | Тесты в том же MR; строки тестов / строки кода (общие с C#; по [18] 0.33 против 0.19) | И | В | У | CLI-скелеты не считать |
| test.rtlQueries | `getByRole/findByRole/getByLabelText` / все запросы (`getByTestId`, `container.querySelector`, Enzyme `wrapper.find`) | И | С–В | Ч | `testing-library/prefer-*`; Enzyme — время |
| test.userEvent | `userEvent.setup()` + `await user.click` против `fireEvent` | И | С | Ч | user-event v14 (2022) |
| test.clearMocks | `beforeEach(() => jest/vi.clearAllMocks())` / тест-файлы с моками | И | С | Ч | `clearMocks: true` в jest.config |
| test.each | `it.each`, `test.each`, `describe.each` / тест-файлы | И | С | Ч | `jest/prefer-each` |
| test.titles | Единообразие заголовков (все `should …` или все `renders/calls … when …`), доля заголовков о граничных случаях (empty, null, error, invalid, when) | И | С | Ч | `jest/valid-title`; CLI «should create» |
| test.aaa | `// Arrange / Act / Assert`, `// Given / When / Then` | И | С–В (редко) | Л | — |
| test.exampleData | `test@example.com`, `John Doe`, `Jane Smith`, `Test User`, `'user-1'`, `password123` | И | С | Ч | — |
| test.oracles | Сильные утверждения (`toBe(v)`, `toEqual`, `toHaveBeenCalledWith`, `toHaveTextContent`, `toThrow('msg')`) / все expect | И | С | Ч | Подтверждено [19]; CLI-скелет исключить |
| test.snapshot | `toMatchSnapshot`/`toMatchInlineSnapshot` | Ч | С | Ч | — |
| test.mocked | `jest.mocked`/`vi.mocked`, `mockResolvedValueOnce`, `as jest.Mocked<…>` | И | Н–С | Ч | Время (2021–2022) |

### 2.17 Форма MR и процесс

| id | Признак | → | Сила | Маск. | Риск |
|---|---|---|---|---|---|
| mr.componentKit | Новый компонент приходит «комплектом»: `types.ts`, `constants.ts`, `hooks/use*.ts`, `index.ts`, тест, `*.stories.*`, `*.module.css`. Знаменатель — новые компоненты | И | С–В | У | Генераторы (Nx, plop) — по базе |
| mr.barrel | Новые `index.ts` только с ре-экспортами | И | С | Ч | Конвенция проекта |
| mr.stories | Сторис к каждому новому компоненту (Storybook есть в проекте) | И | С | Ч | — |
| mr.multiPurpose | Несвязанные цели в одном MR (фича + рефакторинг соседнего + тесты + стиль): число слоёв или типов файлов и разнотипные коммиты. По [14] 39.9% против 12.2% | И | С | У | — |
| mr.docs, mr.newFiles, mr.layers, mr.uniformity | Общие с C# | И | как в C# | — | — |
| proc.* | speed, fileGrowth, fixups, commitStyle, reviewResponse, description (по [14] 355 против 56 слов) — общие с C# | — | как в C# | У | — |

## 3. Риски ложных срабатываний и поправки

### 3.1 Конвенции: правило → какие признаки гасит

Конвенции ищем лексически: JS-конфиги без Node.js не выполнить. Файлы: `.eslintrc.*`, `eslint.config.*`, `package.json#eslintConfig`, `.prettierrc*`, `prettier.config.*`, `package.json#prettier`, `biome.json`, `.stylelintrc*`, `tsconfig*.json` (с `extends`), `jest.config.*`, `vitest.config.*`, `.editorconfig`, husky, lint-staged. Ищем имена правил с уровнем `error`/`warn`/`2`/`1` рядом. Известные пресеты (`airbnb`, `standard`, `@typescript-eslint/recommended|strict|stylistic(-type-checked)`, `plugin:@angular-eslint/recommended`, `plugin:vue/vue3-recommended`, `plugin:jsx-a11y/recommended`, `next/core-web-vitals`, `react-app`) раскрываем по встроенной таблице «пресет → правила»; состав пресетов сверить по версиям плагинов. Множитель веса — как `ConventionFactor` в C#. husky + lint-staged или шаг линта в CI означают, что правило реально соблюдается: множитель строже.

| Конвенция | Ослабляет | Множитель |
|---|---|---|
| Prettier / Biome / dprint | `fmt.*`, `habits.mixedStyle` | ×0 |
| `@typescript-eslint/explicit-function-return-type`, `explicit-module-boundary-types` | `ts.returnTypes` | ×0.2 |
| `@typescript-eslint/no-explicit-any` (в recommended v6+) | `ts.any`, частично `ts.doubleCast` | ×0.3 |
| `@typescript-eslint/no-non-null-assertion` (strict) | `ts.nonNull` | ×0.3 |
| `@typescript-eslint/ban-ts-comment` | `ts.tsIgnore`, часть `habits.markers` | ×0.3 |
| `prefer-nullish-coalescing`, `prefer-optional-chain` | `syn.nullish`, `syn.optChain` | ×0.3 |
| `consistent-type-imports`; `verbatimModuleSyntax`/`isolatedModules` в tsconfig | `ts.importType` | ×0 |
| `consistent-type-definitions`, `array-type` | interface/type, `T[]`/`Array<T>` | ×0 |
| `@typescript-eslint/naming-convention` | `name.iPrefix`, `name.underscorePrivate`, `name.boolState`, `name.constCase` | ×0.3 |
| `eqeqeq` | `syn.eqeq` | ×0 |
| `no-var`, `prefer-const` | `syn.var` | ×0.2 |
| `prefer-template` | `syn.concat` | ×0.2 |
| `radix` | `syn.radix` | ×0 |
| `curly`, `brace-style`, `@stylistic/*` | `fmt.*` | ×0.5 |
| `no-console` (часто `allow: ['warn', 'error']`) | `log.consoleLog` ×0.5. `log.errorTemplate` не трогать, если error разрешён | — |
| `no-debugger`, `no-alert` | часть `habits.markers` | ×0.5 |
| `react/button-has-type`, `@angular-eslint/template/button-has-type` | `a11y.buttonType` | ×0.2 |
| `react/no-array-index-key` | `react.keyIndex` | ×0.3 |
| `react/function-component-definition` | `react.fcType` | ×0.3 |
| `jsx-a11y/*`, `@angular-eslint/template/accessibility-*`, `vuejs-accessibility` | `a11y.*` | ×0.3 |
| `@angular-eslint/prefer-on-push-component-change-detection`, `prefer-standalone`, `prefer-inject`, `prefer-signals`, `use-lifecycle-interface`, `template/use-track-by-function` | `ng.onPush`, `ng.modernApis`, `ng.lifecycleIfaces`, `ng.trackBy` | ×0.2 |
| `vue/define-props-declaration`, `define-emits-declaration`, `component-api-style`, `block-lang`, `require-v-for-key`, `no-use-v-if-with-v-for` | `vue.*` | ×0.2 |
| `jest/valid-title`, `prefer-lowercase-title`, `consistent-test-it`, `prefer-each` | `test.titles`, `test.each` | ×0.3 |
| `testing-library/prefer-screen-queries`, `prefer-user-event`, `no-node-access`, `no-container` | `test.rtlQueries`, `test.userEvent` | ×0.3 |
| `unused-imports/no-unused-imports`, TS `noUnusedLocals` | `habits.unusedImports` | ×0 |
| stylelint: `declaration-no-important`, `max-nesting-depth`, `selector-class-pattern`, `declaration-strict-value`, `color-no-hex`, `unit-allowed-list` | `css.important`, `css.nesting`, `css.vars`, `css.llmValues`, `css.misc` | ×0.3 |
| `eslint-plugin-jsdoc` (`require-jsdoc`), typedoc/compodoc в devDeps | `doc.*` | ×0.2 |
| `react-hooks/exhaustive-deps` включено | `async.depsDisable` становится информативнее (обход правила) | ×1.2 |

### 3.2 tsconfig

- **`strict` / `noImplicitAny`** заставляет типизировать. `ts.genericState` ×0.5, `ts.any` смысл меняет: `any` пишут явно.
- **`useUnknownInCatchVariables`** (входит в strict с TS 4.4) делает сужение ошибки обязательным: `err.instanceofMessage` ×0.5. Форма `instanceof Error ? … : 'Unknown error'` всё равно сильнее, чем `(e as Error)`.
- **`noUnusedLocals`/`noUnusedParameters`** → `habits.unusedImports` ×0.
- **`strictPropertyInitialization`**: `foo!: T` в Angular естественен, в `ts.nonNull` не входит.
- **`target`/`lib`** определяют доступность `syn.newApis`.
- **`erasableSyntaxOnly`** (TS 5.8) запрещает enum: `ts.literalUnion` ×0.

### 3.3 Генераторы и сгенерированный код (исключить или снизить вес)

- **Angular CLI (`ng generate`)** — узнаём скелет и помечаем «сгенерированное», а не «ИИ»: `*.spec.ts` с единственным `it('should create', () => expect(component).toBeTruthy())` или `it('should be created')`; пустой `*.component.scss`; `<p>foo works!</p>`; `constructor() { }`, пустой `ngOnInit(): void {}` с `implements OnInit`; `providedIn: 'root'`. Если скелет в MR дописан, считаем только добавленное сверх скелета.
- **Стартовые шаблоны:** CRA (`reportWebVitals`, `setupTests`, `App.test` «renders learn react link»), Vite (счётчик `count` в `App.tsx`, `vite-env.d.ts`), create-vue и Nuxt (`HelloWorld.vue`, `TheWelcome.vue`), create-next-app, Storybook init (`stories/Button.*`, `Header.*`, `Page.*` — с JSDoc и aria, выглядят как ИИ!).
- **Скопированные библиотеки компонентов:** shadcn/ui (`components/ui/*.tsx` при `components.json`: `cn()`, `forwardRef`, `displayName`, `focus-visible:ring`) — сильный риск ложного «ИИ»; аналогично схемы Angular Material и шаблоны PrimeVue.
- **Кодогенерация:** `*.d.ts`, `*.generated.ts`, `*.gen.ts`, `__generated__/`, `generated/graphql.ts`; OpenAPI (openapi-generator — шапки `/* tslint:disable */ /* eslint-disable */`, «Do not edit the class manually»; orval, swagger-typescript-api); ts-proto, Prisma, `routeTree.gen.ts` (TanStack Router), svgr-иконки.
- **Сборка и данные:** `dist/`, `build/`, `.next/`, `*.min.js`, `*.map`, `vendor/`, чужие библиотеки в `public/`, lock-файлы, `__snapshots__/*.snap`, i18n-JSON (`locales/`, `assets/i18n/`), `*.svg`.
- **Механические миграции** дают сплошь «современный» однообразный код без ИИ — ложные `ng.modernApis`, `vue.modernApis`, `mr.uniformity`: `ng update` и миграции Angular (`@angular/core:control-flow`, `:inject`, `:signal-input-migration`, `:standalone`), react-codemod, `@next/codemod`, `eslint --fix`, `prettier --write`, переименования в IDE. Признаки: заголовок или коммит со словами migrate / ng update / codemod / format; много файлов с маленькими однотипными диффами; низкая доля новых строк. Такие MR помечать и снижать вес синтаксических групп.

### 3.4 Временной перекос

Человеческий корпус — код до 2021 года. ИИ-корпус пишется на коммитах 2020 года: Claude смотрит на `package.json`, но может использовать то, что компилируется. В дикой выборке 2025–2026 годов новые API пишут и люди. Правила:
1. Если API недоступен в версии проекта, признак не считать. Версии берём из `package.json`, `tsconfig`, browserslist.
2. «Модные» признаки считать **только** как сдвиг относительно базовой линии (старая версия файла и соседние файлы), с ограничением группы.
3. Дикую выборку калибровать и оценивать отдельно.

| API / идиома | С какой версии (дата) | Комментарий |
|---|---|---|
| `?.`, `??` | TS 3.7 (11.2019) | В коде до 2020 года редки — только сдвиг |
| `import type` | TS 3.8 (02.2020) | — |
| `unknown` в catch по умолчанию | TS 4.4 (08.2021) | Сужение ошибки стало обязательным |
| `satisfies`, `const`-параметры типов, `using`, `erasableSyntaxOnly` | TS 4.9 (11.2022), 5.0, 5.2, 5.8 (2025) | — |
| Новый JSX-трансформ (без `import React`) | React 17 (10.2020) | `react.legacy` |
| `useId`, `useTransition`, `useSyncExternalStore` | React 18 (03.2022) | — |
| Документация react.dev: флаг `ignore`, «You might not need an effect» | 03.2023 | Люди после 2023 года пишут те же идиомы — `async.abort` слабее в дикой выборке |
| `use`, `useActionState`, `useOptimistic`, ref как prop (без forwardRef) | React 19 (12.2024) | — |
| React Compiler 1.0 | 10.2025 | `useMemo`/`useCallback` теряют смысл, направление `react.memoDensity` меняется |
| `inject()`, standalone, типизированные формы | Angular 14 (06.2022) | — |
| signals, `DestroyRef`, `takeUntilDestroyed` | Angular 16 (05.2023) | — |
| `@if`/`@for`/`@defer` | Angular 17 (11.2023) | — |
| `input()`/`output()`/`model()` | Angular 17.1–17.3 (2024) | — |
| standalone по умолчанию, zoneless, `linkedSignal`, Signal Forms | Angular 19–21 (2024–2025) | — |
| Vue 3, `<script setup>`, `defineModel`, деструктуризация props | 09.2020, 3.2 (08.2021), 3.4 (12.2023), 3.5 (09.2024) | — |
| RxJS 7 (observer-объект вместо позиционных колбэков) | 2021 | `ng.subscribeObserver` |
| user-event v14 `setup()`, `jest.mocked`, Vitest 1.0 | 2022, 2021, 12.2023 | — |
| flex `gap`, `:focus-visible`, `:has()`, container queries, нативная вложенность CSS | 2021–2023 | — |
| палитра Tailwind v2 | 11.2020 | — |
| `??=`, `.at()`, `structuredClone`, `Object.hasOwn`, `toSorted`, `findLast`, `crypto.randomUUID` | ES2021–ES2023 (2021–2023) | — |

### 3.5 Шаблонный код (вес 0.5, как DTO/DI в C#)

- **Конфиги и каркас:** `vite/webpack/jest/vitest/tailwind/postcss/eslint.config.*`, `angular.json`, `nuxt.config.ts`, `environment*.ts`; роуты (`app.routes.ts`, `routes.tsx`, `router/index.ts`); модули и провайдеры (`app.module.ts`, `app.config.ts`, `main.ts`).
- **Сторы:** Redux Toolkit (`createSlice`, `createAsyncThunk` с `pending/fulfilled/rejected` — тройка «загрузка/ошибка» задана библиотекой, `err.loadingTriad` не считать), NgRx (`createAction('[Users] Load')`, effects), модули Vuex, Pinia.
- **Прочее:** i18n-словари, константы и enum-файлы, `types.ts` только с интерфейсами (JSDoc-признаки там слабее), моки, фикстуры и MSW-хендлеры, сторис в формате CSF (`export default { title, component }`, `args`), barrel-файлы.

### 3.6 Прочие риски

- **Строгие команды выглядят как ИИ.** Senior-команды и дизайн-системы (a11y, JSDoc, строгий TS, Testing Library) похожи на ИИ в синтаксисе. Различают только привычки, процесс и форма MR. Порог калибровать так, чтобы ни один человеческий MR не получил ≥ 65%.
- **Копии из документации** (react.dev, angular.dev, примеры MDN) содержат «правильные» идиомы: флаг `ignore`, проверку `response.ok`.
- **Загрязнённый «человеческий» класс.** Люди 2024–2026 годов пишут с Copilot и Cursor, а гомогенизация стиля доказана [21]. Задача формально бинарна (человек или Claude Code), но ассистированный человеческий код будет «серым».
- **Маленькие MR.** Порог «недостаточно кода» для JS/TS — около 40–60 LOC скриптов. Разметку и стили считать отдельно и не смешивать в один LOC.
- **Смешанное авторство** [6][10]. Если Claude правит существующие файлы, сигналы разбавлены — опираться на новые файлы и сдвиг.

## 4. Перспективные и слабые признаки

### 4.1 Топ-15 (по ожидаемой силе × устойчивости × частоте)

1. **Артефакты MR** (общие): трейлеры, «Generated with Claude Code», `claude/`-ветка, `CLAUDE.md`. Решающие, когда есть.
2. **`async.effectCleanup` + `async.abort` / `async.ngTeardown` / `async.vueCleanup`** — корректность отмены и отписки, прямой аналог CancellationToken; устойчив к маскировке.
3. **`log.errorTemplate`** — `console.error('Failed to …:', error)`: частый у LLM, редкий в коде до 2021 года.
4. **`err.instanceofMessage`** — с поправкой на TS ≥ 4.4 strict.
5. **`err.responseOk` + `err.failedMsg`** — валидация на границе: Claude Code делает её даже по своему промпту.
6. **`habits.markers`** (`console.log`, `debugger`, TODO, закомментированный код, `eslint-disable`, `@ts-ignore`) — за человека; в C# один из сильнейших.
7. **`mr.testsWithCode` / `test.ratio`** — подтверждено [14][17][18].
8. **Стиль тестов:** `test.rtlQueries`, `test.oracles` [19], `test.clearMocks`, `test.each`, `test.exampleData`.
9. **`cmt.jsxSection` / `cmt.narrative`** — когда комментарии вообще есть; подтверждено [12]; маскируется.
10. **`name.catchParam` + `name.lambdaParam` + `name.abbrev`** — `error` против `e/err`, `users.map((user) =>` против `x =>`.
11. **`a11y.buttonType` + `a11y.ariaLabelIcon` + `a11y.labelFor`**, если нет соответствующих правил линтера.
12. **`ts.nonNull`, `ts.tsIgnore` (Ч) против `ts.unknown`, `ts.utility`, `ts.typeGuard`, `ts.literalUnion` (И)** — по [16] у Claude Code максимум «продвинутых» типов.
13. **Фреймворк-специфичные идиомы корректности и типизации:** `ng.subscribeObserver`, `ng.typedHttp`, `ng.ctorReadonly`, `ng.nestedSubscribe`; `vue.propsTyped`, `vue.emitsTyped`, `vue.withDefaults`; `react.keyIndex`, `react.derivedStateEffect`, `syn.functionalUpdate`.
14. **`mr.componentKit` + `mr.multiPurpose`** — форма MR: устойчиво, подтверждено [14].
15. **Процесс** (общий): `proc.fileGrowth`, `proc.fixups`, `proc.speed`, `proc.description`.

### 4.2 Вероятно слабые или опасные

- **Синтаксис, который выравнивают Prettier и ESLint:** `fmt.*` с Prettier, `syn.eqeq`, `syn.var`, `syn.concat`, `ts.importType`, interface или type, стрелки против `function`, `export default`, кавычки и точки с запятой.
- **`ts.returnTypes`** — неоднозначный, зависит от линтера; **`ts.any`** — направление противоречиво ([16] против интуиции).
- **«Модные» API** (`ng.modernApis`, `vue.modernApis`, `syn.newApis`, `satisfies`, `react.legacy`) — почти чистое время; только сдвиг с малым весом.
- **Признаки с неустойчивым или неизвестным направлением:** `react.memoDensity` (меняется с React Compiler), `react.inlineStyle`, `name.length` (слаб уже в C#), `doc.coverage` и `cmt.density` у Claude Code (промпт подавляет), `css.misc` (768px, rem, gap), `ng.lifecycleIfaces`, `providedIn: 'root'`, `$`-суффикс, `<style scoped>`.
- **`a11y.ariaDensity`** — направление не ясно [25].
- **`css.llmValues`** — сильный в «чистом» CSS, но ×0 в проектах на Tailwind и при палитре Tailwind.

### 4.3 Что замерить первым на корпусе

- Разделимость (AUC по одному признаку): `log.errorTemplate`, `err.*`, `async.*`, `habits.markers`, `name.catchParam`, `cmt.jsxSection`, `test.rtlQueries`, `test.oracles`. Направление `ts.any` и `a11y.ariaDensity`.
- Как часто у Claude Code вообще встречаются комментарии и JSDoc: обычный режим против маскировки.
- Ложные срабатывания на скелетах CLI, shadcn/ui, сторис и миграциях.
- Дикая выборка: AIDev (Claude Code, JS/TS) [15] и поиск коммитов «Generated with Claude Code» — оценивать с `--no-artifacts`.

## 6. Что подтвердилось на корпусе (ноябрь 2026)

Корпус:
- **люди:** 154 MR до 2021 года и 73 MR 2022–2024 годов из 22 проектов;
- **ИИ:** 22 MR агентов Claude Code (14 обычных, 8 замаскированных) и 57 настоящих коммитов Claude Code.

Метрики — в [EVALUATION.md](EVALUATION.md).

1. **Подтвердилось сильнее всего** — форма MR и процесс, общие с C#:
   - тесты в том же MR и их доля, доля новых файлов;
   - новые файлы «с первого раза», нет коммитов-доделок, скорость, стиль сообщений коммитов.

   Это группы, устойчивые к маскировке.
2. **Подтвердились на коде:**
   - аккуратность комментариев (Claude почти не пишет небрежных `//todo`, `//без пробела`);
   - `console.error('…:', error)` против `console.log`;
   - точные проверки в тестах (`toEqual`, `toHaveBeenCalledWith`);
   - явные типы результата у экспортируемых функций;
   - `type="button"`;
   - отписка от подписок в Angular;
   - `instanceof Error` и `response.ok`;
   - отсутствие «ручных» следов (TODO, закомментированный код, `debugger`, `eslint-disable`).
3. **Как и предсказывал раздел 1.4, системный промпт Claude Code подавляет классические «следы LLM».**
   - JSDoc, повествовательные комментарии и заголовки секций у агентов встречаются редко. Детектор считает их признаком ИИ, только когда они есть, а их отсутствие нейтрально (`humanAt = −aiAt`).
   - На корпусе это было важно: иначе каждый MR без AAA-комментариев или без `unknown` получал свидетельство «за человека».
4. **Не подтвердилось или оказалось обратным** — калибровка обнулила или снизила:
   - сложность и вложенность функций, «магические» числа;
   - `ng.ctorReadonly`;
   - однобуквенные параметры колбэков (`x =>`) и сокращения — агенты подстраиваются под проект.
5. **Ложные срабатывания на Angular:**
   - один компонент — это 5 файлов (`.ts`, `.html`, `.scss`, `spec`, модуль), поэтому признак «много слоёв» завышался. Шаблоны, стили и модули больше не считаются отдельными слоями;
   - скелеты `ng generate` (`spec` с одним «should create») исключаются как сгенерированные.
6. **Поправка на время подтвердилась.**
   - Без современного человеческого корпуса детектор частично учился бы различать «старое против нового».
   - С ним AUC дикой выборки против людей 2022–2024 годов — 0.91, против людей до 2021 — 0.94. Разрыв небольшой, значит, детектор различает в основном именно Claude.
7. **Маскировка:**
   - «пиши как в проекте» прячет Claude почти полностью (AUC 0.60);
   - дольше всего держатся процесс и корректность отписки — в соответствии с разделом 4.1.

## 5. Источники

1. Nguyen P. T. et al. GPTSniffer: A CodeBERT-based classifier to detect source code written by ChatGPT. JSS, 2024. https://github.com/MDEGroup/GPTSniffer ; https://conf.researchr.org/details/esem-2024/esem-2024-journal-first/2/GPTSniffer-A-CodeBERT-based-classifier-to-detect-source-code-written-by-ChatGPT
2. Suh H. et al. An Empirical Study on Automatically Detecting AI-Generated Source Code: How Far Are We? ICSE 2025. https://arxiv.org/abs/2411.04299
3. Shi Y. et al. Between Lines of Code: Unraveling the Distinct Patterns of Machine and Human Programmers (DetectCodeGPT). ICSE 2025. https://arxiv.org/abs/2401.06461 ; https://github.com/YerbaPage/DetectCodeGPT
4. Distinguishing LLM-generated from Human-written Code by Contrastive Learning (CodeGPTSensor). 2024. https://arxiv.org/abs/2411.04704
5. Orel D., Azizov D., Nakov P. CoDet-M4. Findings of ACL 2025. https://arxiv.org/abs/2503.13733
6. Demirok B., Kutlu M. AIGCodeSet. 2024. https://arxiv.org/abs/2412.16594
7. Gurioli A., Gabbrielli M., Zacchiroli S. Is This You, LLM? Recognizing AI-written Programs with Multilingual Code Stylometry. SANER 2025. https://arxiv.org/abs/2412.14611
8. The Hidden DNA of LLM-Generated JavaScript: Structural Patterns Enable High-Accuracy Authorship Attribution. 2025. https://arxiv.org/abs/2510.10493
9. Rahman M. et al. Automatic Detection of LLM-generated Code: A Case Study of Claude 3 Haiku. 2024. https://arxiv.org/abs/2409.01382
10. Orel D. et al. Droid: A Resource Suite for AI-Generated Code Detection. EMNLP 2025. https://arxiv.org/abs/2507.10583
11. SemEval-2026 Task 13: Detecting Machine-Generated Code; UCSC-NLP system paper. https://arxiv.org/abs/2604.26990 ; https://www.aclweb.org/portal/node/14325
12. Yotkova E., Kastreva V., Nakov P. Lightweight / Interpretable Stylometric Detection of LLM-Generated Code (SemEval-2026; NeurIPS 2026 WiML). https://arxiv.org/abs/2605.04157 ; https://neurips.cc/virtual/2026/157911
13. Ye T. et al. Uncovering LLM-Generated Code: A Zero-Shot Synthetic Code Detector via Code Rewriting. AAAI 2025. https://arxiv.org/abs/2405.16133
14. Watanabe M. et al. On the Use of Agentic Coding: An Empirical Study of Pull Requests on GitHub (Claude Code). TOSEM. https://arxiv.org/abs/2509.14745
15. Li H., Zhang H., Hassan A. E. The Rise of AI Teammates in SE 3.0 / AIDev: Studying AI Coding Agents on GitHub (MSR’26). https://arxiv.org/abs/2507.15003 ; https://arxiv.org/abs/2602.09185 ; https://github.com/SAILResearch/AI_Teammates_in_SE3
16. Lee, Ul Hassan, Hindle. Mining Type Constructs Using Patterns in AI-Generated Code. MSR’26. https://arxiv.org/abs/2602.17955
17. Haque, Ingale, Csallner. Do Autonomous Agents Contribute Test Code? / An Empirical Study of Tests in Agentic Pull Requests. MSR’26. https://arxiv.org/abs/2601.03556
18. Milanese et al. Human-Agent versus Human Pull Requests (testing). 2026. https://arxiv.org/abs/2601.21194
19. Banik et al. All Smoke, No Alarm: Oracle Signals in Agent-Authored Test Code. 2026. https://arxiv.org/abs/2606.18168
20. Cotroneo D., Improta C., Liguori P. Human-Written vs. AI-Generated Code: A Large-Scale Study of Defects, Vulnerabilities, and Complexity. ISSRE 2025. https://arxiv.org/abs/2508.21634
21. Burtch G. The Hitchhiker’s Guide to Monoculture. 2026. https://arxiv.org/abs/2607.13077 ; Wu F. et al. Generative Monoculture in LLMs. ICLR 2025. https://arxiv.org/abs/2407.02209
22. Системный промпт Claude Code (реконструкции сообщества, не официальный текст). https://claude-code-sourcemap-site.pages.dev/prompts/03-doing-tasks/en ; https://gist.github.com/sergeyk/b1eb7bf8fd8db2566c082409dbd3eca9 ; официальная документация output styles: https://docs.anthropic.com/en/docs/claude-code/output-styles
23. Angular: Develop with AI — best practices для LLM. https://angular.dev/ai/develop-with-ai ; правила angular-eslint: https://github.com/angular-eslint/angular-eslint/blob/main/packages/eslint-plugin/README.md
24. eslint-plugin-testing-library (prefer-screen-queries, prefer-user-event). https://github.com/testing-library/eslint-plugin-testing-library
25. Suh et al. Accessibility of LLM-generated web code (FeedA11y). 2025. https://arxiv.org/abs/2503.15885
26. Fu Y. et al. Security Weaknesses of Copilot-Generated Code in GitHub Projects. TOSEM 2025. https://arxiv.org/abs/2310.02059
27. Навыки «deslop/rmslop» (удаление ИИ-мусора из диффа). https://skills.cat/skills/brianlovin/claude-config/deslop ; https://smithery.ai/skills/revokslab/rmslop ; https://skills.cat/skills/v1-io/v1tamins/deslop
28. Detecting AI-Generated Code (чек-лист сигналов). https://www.skills.lc/galihcitta/dotclaudeskills/galihcitta-dotclaudeskills-skills-detecting-ai-code-skill-md
29. Блоги о React-коде ИИ. https://levelup.gitconnected.com/thank-you-for-explaining-setloading-false-to-me-38bcb75839f4 ; https://levelup.gitconnected.com/the-smell-of-ai-generated-code-89050b013427
30. «AI slop» во фронтенд-дизайне (индиго Tailwind, Inter). https://dev.to/alanwest/why-every-ai-built-website-looks-the-same-blame-tailwinds-indigo-500-3h2p ; https://prg.sh/ramblings/Why-Your-AI-Keeps-Building-the-Same-Purple-Gradient-Website
31. Типичные ошибки LLM во Vue 3 (правила Cursor). https://dev.to/olivia_craft/cursor-rules-for-vue-3-6-rules-that-make-ai-write-composable-type-safe-vue-4oef
32. React: You Might Not Need an Effect; Fetching data with Effects (флаг `ignore`). https://react.dev/learn/you-might-not-need-an-effect ; https://react.dev/reference/react/useEffect#fetching-data-with-effects
