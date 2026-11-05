# Прогресс ночной работы

> Актуальный и полный конспект проекта — [CONTEXT.md](CONTEXT.md). Этот файл — исторический журнал первой ночи.

Ветка: `claude/inspiring-lamport-i2ixa7`. .NET SDK в контейнере: `/root/.dotnet` (`export PATH=/root/.dotnet:$PATH`).
Если сессия была прервана лимитом — продолжать с первого невыполненного пункта.

## Сделано
- [x] Окружение: .NET 10 SDK, Roslyn, push в GitHub
- [x] Решение: `src/AiDetect.Core` (движок), `src/AiDetect.Cli` (консоль + веб), `tests/AiDetect.Tests` (45+ тестов)
- [x] Источники MR: GitLab API v4 (параллельная загрузка, фолбэк `/changes` для старых GitLab, ретраи с лимитом, `--insecure`, cp1251), локальный git, JSON
- [x] Предобработка: исключение сгенерированного (Designer, g.cs, Migrations, scaffold Identity), Roslyn-разбор добавленных строк
- [x] ~75 признаков в 16 группах (см. README), поправка на код проекта (базовая линия) и на конвенции (StyleCop, editorconfig, commitlint, шаблоны MR)
- [x] Скоринг: вклад групп с ограничением, температура, консервативное смещение; калибровка `calibrate` (L2 к экспертным весам, CV)
- [x] Отчёт: вероятность, вердикт, надёжность, краткое объяснение, главные признаки ИИ/человека с примерами `файл:строка`, оценка по файлам, вклад групп; консоль/HTML/JSON
- [x] Режимы: анализ ссылки, `web`, `batch`, `fetch`, `--git`, `--load`, `eval`, `calibrate`, `--no-artifacts`
- [x] `run.sh`: автоустановка .NET в `~/.dotnet`, сборка, запуск (проверено со свежего клона)
- [x] Корпус: человеческие MR из 19 публичных C#-репозиториев до 2021 года + ИИ-образцы Claude (обычные и замаскированные) — `docs/EVALUATION.md`
- [x] Проверка на настоящем GitLab (gitlab.com, публичные MR), веб-режим

## Что делать дальше (рекомендации)
1. Запустить на своих MR (`./run.sh <ссылка>` или `./run.sh batch mrs.txt`).
2. Разметить 20–50 своих MR (человек / Claude) → `./run.sh calibrate corpus-my -o model/weights.json` — главный рычаг точности.
3. Если есть телеметрия Claude Code (Analytics API / OpenTelemetry) — использовать как эталонную разметку (см. исследование).

## Заметки для продолжения
- Сборка: `dotnet build src/AiDetect.Cli -c Release -o .build`
- Полный цикл: `scripts/evaluate.sh [--refresh]` (корпус JSON не коммитится: `scripts/build_corpus_from_git.py`, `scripts/refresh_corpus.py`)
- Клоны публичных репо: `/home/user/<owner>/<repo>` (partial clone); ИИ-образцы — ветки `ai/<имя>` в этих клонах, патчи в `corpus/ai-src/`
- Хелпер ИИ-образцов: `scripts/make_ai_sample.sh start|commit|finish` (при правке существующих файлов сохранять CRLF!)
