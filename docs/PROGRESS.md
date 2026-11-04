# Прогресс ночной работы

Ветка: `claude/inspiring-lamport-i2ixa7`. .NET SDK в контейнере: `/root/.dotnet` (export PATH=/root/.dotnet:$PATH).
Если сессия была прервана лимитом — продолжать с первого невыполненного пункта.

## Этапы
- [x] Окружение: .NET 10 SDK, Roslyn, push в GitHub — проверено
- [x] Каркас решения: `src/AiDetect.Core`, `src/AiDetect.Cli`, `tests/AiDetect.Tests`
- [x] Модель данных MR, парсер диффа, классификатор файлов
- [x] Извлечение признаков C# через Roslyn (`Features/CSharpFeatureExtractor.cs`)
- [x] Сигналы и скоринг (группы, веса, ограничение вклада групп)
- [x] MR-уровень: артефакты, форма диффа, коммиты, скорость, описание
- [x] Базовая линия проекта (сдвиг)
- [x] Источники: GitLab API, локальный git, JSON
- [x] Отчёт: консоль (ANSI), HTML, JSON
- [x] CLI + `--web`
- [x] run.sh + README (запуск на Mac)
- [x] Модульные тесты
- [x] Корпус (79 human из 12 репо + 14 ИИ) и оценка (docs/EVALUATION.md), калибровка весов → model/weights.json (lambda=8)
- [x] Проверка GitLab-клиента на gitlab.com (публичный MR, 14 с, параллельная загрузка), веб-режим проверен
- [~] Доп. критерии и улучшения: конвенции проекта (StyleCop/editorconfig/commitlint/шаблоны MR), явные модификаторы, вложенность, общие исключения, русские комментарии-повествования, опечатки

## Заметки для продолжения
- Сборка: `export PATH=/root/.dotnet:$PATH; dotnet build src/AiDetect.Cli -c Release -o .build`
- Оценка: `dotnet .build/aidetect.dll eval corpus -v` (corpus/human, corpus/ai-default, corpus/ai-masked — JSON не коммитятся, пересобираются скриптами)
- Клоны публичных репо: /home/user/<owner>/<repo> (partial clone, blob:limit=150k)
- Хелпер ИИ-образцов: scratchpad/mk_ai.sh start|commit|finish
