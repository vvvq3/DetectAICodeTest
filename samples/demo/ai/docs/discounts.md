# Discounts

## Overview

The discounts feature allows customers to apply promotional codes to their orders.

## Configuration

```json
"Discounts": {
  "MaxPercent": 50
}
```

## API

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/discounts` | List active discounts |
| POST | `/api/orders/{orderId}/discount` | Apply a discount code |
| DELETE | `/api/discounts/{id}` | Deactivate a discount |

## Notes

- Codes are case-insensitive.
- The percentage is capped by `MaxPercent`.
