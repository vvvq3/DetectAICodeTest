#!/usr/bin/env bash
# Создаёт демо-репозиторий с двумя ветками одной фичи «скидки»:
#   feature/human-discounts — написана вручную;
#   feature/ai-discounts    — написана в стиле Claude Code (без трейлеров и подписей).
# Использование: scripts/make-demo-repo.sh /tmp/demo-repo
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/samples/demo"
DEST="${1:-/tmp/aidetect-demo}"
rm -rf "$DEST" && mkdir -p "$DEST" && cd "$DEST"
git init -q -b main
git config user.name "Ivan Petrov"; git config user.email "ivan@example.local"
commit() { GIT_AUTHOR_DATE="$1" GIT_COMMITTER_DATE="$1" git commit -q -m "$2"; }

cp -R "$SRC/base/." . && git add -A && commit "2024-03-01T10:00:00+02:00" "initial: orders api"

git checkout -q -b feature/human-discounts
cp "$SRC/human/src/Shop.Api/Models/Discount.cs" src/Shop.Api/Models/
git add -A && commit "2024-03-05T11:20:00+02:00" "модель скидки"
cp "$SRC/human/src/Shop.Api/Services/DiscountService.cs" src/Shop.Api/Services/
printf '\nbuilder.Services.AddScoped<DiscountService>();\n' >> src/Shop.Api/Program.cs
git add -A && commit "2024-03-05T15:47:00+02:00" "добавил сервис скидок"
cp "$SRC/human/src/Shop.Api/Controllers/OrdersController.cs" src/Shop.Api/Controllers/
git add -A && commit "2024-03-06T10:05:00+02:00" "эндпоинты для скидок"
sed -i.bak 's/discount {code} expired/discount {code} expired or inactive/' src/Shop.Api/Services/DiscountService.cs && rm -f src/Shop.Api/Services/DiscountService.cs.bak
git add -A && commit "2024-03-06T12:30:00+02:00" "fix"

git checkout -q main
git checkout -q -b feature/ai-discounts
cp -R "$SRC/ai/." .
sed -i.bak 's/builder.Services.AddScoped<OrderService>();/builder.Services.AddScoped<OrderService>();\nbuilder.Services.AddDiscounts(builder.Configuration);/' src/Shop.Api/Program.cs && rm -f src/Shop.Api/Program.cs.bak
git add src docs && commit "2024-03-05T11:20:00+02:00" "feat(discounts): add discount code support

- Add Discount entity and DiscountOptions
- Implement IDiscountService with validation and usage limits
- Add DiscountsController endpoints
- Register services via AddDiscounts extension
- Document the feature in docs/discounts.md"
git add -A && commit "2024-03-05T11:32:00+02:00" "test(discounts): add DiscountService unit tests

- Cover valid, unknown and expired codes
- Verify percentage cap via theory data"
git checkout -q main
echo "Демо-репозиторий: $DEST"
echo "  ручная ветка:  feature/human-discounts"
echo "  ветка ИИ:      feature/ai-discounts"
