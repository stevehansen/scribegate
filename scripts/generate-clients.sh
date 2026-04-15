#!/usr/bin/env bash
set -euo pipefail

# Generate client libraries from the Scribegate OpenAPI spec.
# Usage: ./scripts/generate-clients.sh [--host http://localhost:5199]
#
# The server must be running to fetch the OpenAPI spec.

HOST="${1:-http://localhost:5199}"
SPEC="clients/openapi.json"

echo "Fetching OpenAPI spec from $HOST..."
curl -s "$HOST/swagger/v1/swagger.json" > "$SPEC"
echo "Saved to $SPEC"

# TypeScript client
echo ""
echo "=== TypeScript Client ==="
cd clients/typescript
npm install --silent 2>/dev/null || true
npm run generate 2>/dev/null && echo "TypeScript client generated." || echo "TypeScript generation requires @hey-api/openapi-ts. Run: cd clients/typescript && npm install"
cd ../..

# C# client
echo ""
echo "=== C# Client ==="
echo "To generate C# client, use NSwag or Kiota:"
echo "  nswag openapi2csclient /input:$SPEC /output:clients/csharp/ScribegateClient.cs"
echo "  OR"
echo "  kiota generate -l CSharp -d $SPEC -o clients/csharp/Generated"

# Python client
echo ""
echo "=== Python Client ==="
echo "To generate Python client:"
echo "  openapi-python-client generate --path $SPEC --output-path clients/python/src/scribegate"

echo ""
echo "Done. Spec saved at $SPEC"
