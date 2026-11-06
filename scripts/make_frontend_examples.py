#!/usr/bin/env python3
"""Примеры отчётов фронтенд-детектора (HTML, PNG, PDF) в docs/examples/frontend по MR из корпуса.
Нужны собранный .build и JSON корпуса (scripts/rebuild_corpus.py --lang frontend). PNG и PDF — scripts/example_shots.js."""
import json, os, re, subprocess
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'docs', 'examples', 'frontend')
WORK = os.environ.get('EXAMPLES_WORK_DIR', '/tmp')
os.makedirs(OUT, exist_ok=True)
examples = [
    ('01-react-vkui-rating', 'ai-default/fe-vkui-rating', 'VKUI (React + TypeScript)', 'feature/rating',
     'Новый компонент Rating: стили, тесты, документация'),
    ('02-angular-spartacus-back-to-top', 'ai-default/fe-spartacus-back-to-top', 'Spartacus (Angular)', 'feature/back-to-top',
     'CMS-компонент «Наверх»: модуль, конфигурация, стили, спецификации'),
    ('03-vue-chatwoot-canned-search', 'ai-default/fe-chatwoot-canned-search', 'Chatwoot (Vue 2)', 'feature/canned-search',
     'Поиск шаблонных ответов в поле ответа: фильтрация, подсветка, клавиатура'),
]
bodies = []
html = ''
for slug, src, project, branch, desc in examples:
    data = json.load(open(os.path.join(ROOT, 'corpus', 'frontend', src + '.json'), encoding='utf-8'))
    data['Source'] = f'{project} · открытый проект · тестовый MR · {desc}'
    data['AuthorName'] = 'разработчик'
    data['AuthorUsername'] = ''
    data['SourceBranch'] = branch
    tmp_json = os.path.join(WORK, slug + '.json')
    json.dump(data, open(tmp_json, 'w', encoding='utf-8'), ensure_ascii=False)
    html_path = os.path.join(OUT, slug + '.html')
    subprocess.run(['dotnet', os.path.join(ROOT, '.build', 'aidetect.dll'), '--load', tmp_json, '--lang', 'frontend', '--html', html_path, '-q'],
                   check=True, capture_output=True)
    html = open(html_path, encoding='utf-8').read()
    banner = ('<div style="display:flex;justify-content:space-between;align-items:baseline;flex-wrap:wrap;gap:8px;margin:4px 2px 2px">'
              '<div style="font-size:13px;letter-spacing:.04em;text-transform:uppercase;color:var(--muted)">Детектор ИИ-кода · фронтенд (JS/TS) · пример отчёта</div>'
              '<div style="font-size:13px;color:var(--muted)">анализ выполнен локально, код никуда не отправлялся</div></div>')
    html = html.replace('<main>', '<main>' + banner, 1)
    open(html_path, 'w', encoding='utf-8').write(html)
    bodies.append(re.search(r'<main>(.*)</main>', html, re.S).group(1))
    print('ok', slug)

style = re.search(r'<style>(.*?)</style>', html, re.S).group(1)
combined = ('<!doctype html><html lang="ru"><head><meta charset="utf-8"><title>Детектор ИИ-кода — фронтенд, примеры отчётов</title><style>' + style +
            '.page{page-break-after:always} .sig,pre,tr,.score{break-inside:avoid} .page:last-child{page-break-after:auto} @media print{main{max-width:none}}</style></head><body><main>' +
            ''.join(f'<section class="page">{b}</section>' for b in bodies) + '</main></body></html>')
open(os.path.join(OUT, 'examples.html'), 'w', encoding='utf-8').write(combined)
