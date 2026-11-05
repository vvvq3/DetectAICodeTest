import json, os, re, subprocess, sys
ROOT = '/home/user/DetectAICodeTest'
OUT = os.path.join(ROOT, 'docs', 'examples')
WORK = os.environ.get('EXAMPLES_WORK_DIR', '/tmp')
examples = [
    ('01-eshop-catalog-reviews', 'ai-default/eshop-catalog-reviews', 'eShopOnContainers', 'feature/catalog-reviews',
     'Фича «отзывы о товарах»: модель, EF-конфигурация, сервис, контроллер и тесты'),
    ('02-bitwarden-folder-cleanup', 'ai-default/bitwarden-folder-cleanup', 'bitwarden/server', 'feature/folder-cleanup',
     'Очистка пустых и дублирующихся папок: сервис, эндпоинты, модель ответа и тесты'),
    ('03-eshopweb-wishlist-project-style', 'ai-masked/eshopweb-wishlist-masked', 'eShopOnWeb', 'feature/wishlist',
     'Список желаний, написанный «в стиле проекта»: без XML-doc, без разметки тестов, короткие коммиты'),
]
bodies = []
for slug, src, project, branch, desc in examples:
    data = json.load(open(os.path.join(ROOT, 'corpus', 'csharp', src + '.json'), encoding='utf-8'))
    data['Source'] = f'{project} (открытый проект) · тестовый MR · {desc}'
    data['AuthorName'] = 'разработчик'
    data['AuthorUsername'] = ''
    data['SourceBranch'] = branch
    tmp_json = os.path.join(WORK, slug + '.json')
    json.dump(data, open(tmp_json, 'w', encoding='utf-8'), ensure_ascii=False)
    html_path = os.path.join(OUT, slug + '.html')
    subprocess.run(['dotnet', os.path.join(ROOT, '.build', 'aidetect.dll'), '--load', tmp_json, '--html', html_path, '-q'],
                   check=True, capture_output=True)
    html = open(html_path, encoding='utf-8').read()
    banner = ('<div style="display:flex;justify-content:space-between;align-items:baseline;flex-wrap:wrap;gap:8px;margin:4px 2px 2px">'
              '<div style="font-size:13px;letter-spacing:.04em;text-transform:uppercase;color:var(--muted)">Детектор ИИ-кода в C# мердж-реквестах · пример отчёта</div>'
              '<div style="font-size:13px;color:var(--muted)">анализ выполнен локально, код никуда не отправлялся</div></div>')
    html = html.replace('<main>', '<main>' + banner, 1)
    open(html_path, 'w', encoding='utf-8').write(html)
    bodies.append(re.search(r'<main>(.*)</main>', html, re.S).group(1))
    print('ok', slug)

style = re.search(r'<style>(.*?)</style>', html, re.S).group(1)
combined = ('<!doctype html><html lang="ru"><head><meta charset="utf-8"><title>Детектор ИИ-кода — примеры отчётов</title><style>' + style +
            '.page{page-break-after:always} .sig,pre,tr,.score{break-inside:avoid} .page:last-child{page-break-after:auto} @media print{main{max-width:none}}</style></head><body><main>' +
            ''.join(f'<section class="page">{b}</section>' for b in bodies) + '</main></body></html>')
open(os.path.join(OUT, 'examples.html'), 'w', encoding='utf-8').write(combined)
