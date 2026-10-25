#!/usr/bin/env python3
"""OpenAPI drift check (BE-9): compares the backend's generated document with contracts/openapi.yaml.

Usage: python3 scripts/openapi-drift.py <generated openapi.json> contracts/openapi.yaml
Requires PyYAML (pip install pyyaml). Exit code 1 on drift.

Checks, for every operation in the contract:
  1. path + method exist in the generated document with the same operationId;
  2. every documented response status code is declared by the backend;
  3. every property the contract marks `required` (request body and 2xx response, recursively through $ref,
     allOf, arrays and nested objects) exists in the generated schema under the same JSON name.
Generated-only paths are reported as warnings (undocumented endpoints).
"""
import json
import sys

import yaml

METHODS = ("get", "post", "put", "patch", "delete")
IGNORED_GENERATED_PATHS = ("/openapi", "/scalar")
MAX_DEPTH = 6


def resolve(doc, schema):
    """Follow $ref and merge allOf into one {properties, required, items} view."""
    seen = 0
    while isinstance(schema, dict) and "$ref" in schema and seen < 20:
        name = schema["$ref"].split("/")[-1]
        schema = doc.get("components", {}).get("schemas", {}).get(name, {})
        seen += 1
    if not isinstance(schema, dict):
        return {}
    if "allOf" in schema:
        merged = {"properties": {}, "required": []}
        for part in schema["allOf"]:
            part = resolve(doc, part)
            merged["properties"].update(part.get("properties", {}))
            merged["required"] += part.get("required", [])
        return merged
    for key in ("oneOf", "anyOf"):
        if key in schema:  # nullable wrappers: take the first non-null branch
            branches = [resolve(doc, b) for b in schema[key]]
            branches = [b for b in branches if b.get("type") != "null"]
            return branches[0] if branches else {}
    return schema


def compare_schema(contract, generated, c_schema, g_schema, where, errors, depth=0):
    if depth > MAX_DEPTH:
        return
    c = resolve(contract, c_schema)
    g = resolve(generated, g_schema)
    if c.get("type") == "array" or "items" in c:
        compare_schema(contract, generated, c.get("items", {}), g.get("items", {}), f"{where}[]", errors, depth + 1)
        return
    g_props = g.get("properties", {})
    for name in c.get("required", []):
        if name not in g_props:
            errors.append(f"{where}: required field '{name}' missing from generated schema")
            continue
        compare_schema(contract, generated, c.get("properties", {}).get(name, {}), g_props[name], f"{where}.{name}", errors, depth + 1)


def json_schema(node, doc):
    content = (node or {}).get("content", {})
    for media in ("application/json", "application/problem+json"):
        if media in content:
            return content[media].get("schema", {})
    return None


def response_node(doc, node):
    if isinstance(node, dict) and "$ref" in node:
        return doc.get("components", {}).get("responses", {}).get(node["$ref"].split("/")[-1], {})
    return node


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    with open(sys.argv[1], encoding="utf-8") as f:
        generated = json.load(f)
    with open(sys.argv[2], encoding="utf-8") as f:
        contract = yaml.safe_load(f)

    errors, warnings, checked = [], [], 0
    for path, item in contract.get("paths", {}).items():
        g_item = generated.get("paths", {}).get(path)
        for method in METHODS:
            if method not in item:
                continue
            op = item[method]
            label = f"{method.upper()} {path}"
            if g_item is None or method not in g_item:
                errors.append(f"{label}: missing from generated document")
                continue
            g_op = g_item[method]
            checked += 1
            if g_op.get("operationId") != op.get("operationId"):
                errors.append(f"{label}: operationId '{g_op.get('operationId')}' != contract '{op.get('operationId')}'")
            missing_codes = set(op.get("responses", {})) - set(g_op.get("responses", {}))
            if missing_codes:
                errors.append(f"{label}: response codes {sorted(missing_codes)} not declared by backend")

            c_body = json_schema(op.get("requestBody"), contract)
            g_body = json_schema(g_op.get("requestBody"), generated)
            if c_body is not None:
                if g_body is None:
                    errors.append(f"{label}: request body missing")
                else:
                    compare_schema(contract, generated, c_body, g_body, f"{label} request", errors)

            for code, c_resp in op.get("responses", {}).items():
                if not code.startswith("2"):
                    continue
                c_schema = json_schema(response_node(contract, c_resp), contract)
                g_schema = json_schema(response_node(generated, g_op.get("responses", {}).get(code)), generated)
                if c_schema is not None and g_schema is not None:
                    compare_schema(contract, generated, c_schema, g_schema, f"{label} {code}", errors)
                elif c_schema is not None:
                    errors.append(f"{label} {code}: response body schema missing")

    for path, item in generated.get("paths", {}).items():
        if path.startswith(IGNORED_GENERATED_PATHS):
            continue
        for method in METHODS:
            if method in item and method not in contract.get("paths", {}).get(path, {}):
                warnings.append(f"{method.upper()} {path}: in backend but not in contract")

    for w in warnings:
        print(f"WARN  {w}")
    for e in errors:
        print(f"DRIFT {e}")
    print(f"{checked} operations checked, {len(errors)} drift error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
