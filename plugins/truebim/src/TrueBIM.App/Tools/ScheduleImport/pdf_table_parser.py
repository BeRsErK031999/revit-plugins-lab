#!/usr/bin/env python
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Parse PDF schedule tables for TrueBIM Schedule Import.")
    parser.add_argument("--input", required=True, help="Source PDF path.")
    parser.add_argument("--output", required=True, help="Output JSON path.")
    args = parser.parse_args(argv)

    result = parse_pdf(Path(args.input))
    write_json(Path(args.output), result)
    return 0


def parse_pdf(source_path: Path) -> dict[str, Any]:
    warnings: list[str] = []
    errors: list[str] = []
    if not source_path.exists():
        return empty_result(errors=[f"PDF-файл не найден: {source_path}"])

    tables = parse_with_camelot(source_path, warnings)
    if not tables:
        tables = parse_with_pdfplumber(source_path, warnings)
    if not tables:
        tables = parse_with_pypdf(source_path, warnings)

    if not tables:
        warnings.append("Таблицы не найдены. Если PDF является сканом, нужен OCR-режим.")

    return {
        "tables": tables,
        "warnings": unique_messages(warnings),
        "errors": unique_messages(errors),
    }


def parse_with_camelot(source_path: Path, warnings: list[str]) -> list[dict[str, Any]]:
    try:
        import camelot  # type: ignore[import-not-found]
    except Exception as exc:
        warnings.append(f"camelot недоступен, режим линий пропущен: {exc}")
        return []

    result: list[dict[str, Any]] = []
    for flavor in ("lattice", "stream"):
        try:
            parsed = camelot.read_pdf(str(source_path), pages="all", flavor=flavor)
        except Exception as exc:
            warnings.append(f"camelot {flavor} не смог разобрать PDF: {exc}")
            continue

        for table_index, table in enumerate(parsed):
            matrix = normalize_matrix(table.df.values.tolist())
            if not is_table_matrix(matrix):
                continue

            accuracy = table.parsing_report.get("accuracy") if hasattr(table, "parsing_report") else None
            confidence = normalize_confidence((float(accuracy) / 100.0) if accuracy is not None else 0.88)
            result.append(to_table_contract(source_path, page_number(table), matrix, confidence, [
                f"parser=camelot:{flavor}",
                f"tableIndex={table_index + 1}",
            ]))

        if result:
            return result

    return result


def parse_with_pdfplumber(source_path: Path, warnings: list[str]) -> list[dict[str, Any]]:
    try:
        import pdfplumber  # type: ignore[import-not-found]
    except Exception as exc:
        warnings.append(f"pdfplumber недоступен, layout-режим пропущен: {exc}")
        return []

    result: list[dict[str, Any]] = []
    try:
        with pdfplumber.open(str(source_path)) as pdf:
            for page in pdf.pages:
                raw_tables = page.extract_tables() or []
                for table_index, raw_table in enumerate(raw_tables):
                    matrix = normalize_matrix(raw_table)
                    if not is_table_matrix(matrix):
                        continue

                    result.append(to_table_contract(source_path, page.page_number, matrix, 0.76, [
                        "parser=pdfplumber:table",
                        f"tableIndex={table_index + 1}",
                    ]))

                if raw_tables:
                    continue

                text = page.extract_text(x_tolerance=2, y_tolerance=3) or ""
                matrix = matrix_from_text(text)
                if is_table_matrix(matrix):
                    result.append(to_table_contract(source_path, page.page_number, matrix, 0.62, [
                        "parser=pdfplumber:text",
                    ]))
    except Exception as exc:
        warnings.append(f"pdfplumber не смог разобрать PDF: {exc}")

    return result


def parse_with_pypdf(source_path: Path, warnings: list[str]) -> list[dict[str, Any]]:
    try:
        from pypdf import PdfReader  # type: ignore[import-not-found]
    except Exception as exc:
        warnings.append(f"pypdf недоступен, текстовый fallback пропущен: {exc}")
        return []

    result: list[dict[str, Any]] = []
    try:
        reader = PdfReader(str(source_path))
        for page_index, page in enumerate(reader.pages):
            text = page.extract_text() or ""
            matrix = matrix_from_text(text)
            if not is_table_matrix(matrix):
                continue

            result.append(to_table_contract(source_path, page_index + 1, matrix, 0.45, [
                "parser=pypdf:text",
                "Проверьте разбиение колонок: использован текстовый fallback без геометрии ячеек.",
            ]))
    except Exception as exc:
        warnings.append(f"pypdf не смог извлечь текст из PDF: {exc}")

    return result


def matrix_from_text(text: str) -> list[list[str]]:
    rows: list[list[str]] = []
    for line in text.splitlines():
        row = split_text_row(line)
        if len(row) >= 2:
            rows.append(row)

    return normalize_matrix(rows)


def split_text_row(line: str) -> list[str]:
    value = clean_value(line)
    if not value:
        return []

    parts = re.split(r"\t+|\|+|\s{2,}", value)
    if len(parts) < 2 and ";" in value:
        parts = value.split(";")

    return [clean_value(part) for part in parts if clean_value(part)]


def normalize_matrix(raw_rows: list[Any]) -> list[list[str]]:
    cleaned: list[list[str]] = []
    for raw_row in raw_rows:
        if raw_row is None:
            continue

        if not isinstance(raw_row, (list, tuple)):
            raw_row = [raw_row]

        row = [clean_value(value) for value in raw_row]
        while row and not row[-1]:
            row.pop()

        if any(row):
            cleaned.append(row)

    if not cleaned:
        return []

    width = max(len(row) for row in cleaned)
    if width == 0:
        return []

    return [row + [""] * (width - len(row)) for row in cleaned]


def is_table_matrix(matrix: list[list[str]]) -> bool:
    return len(matrix) >= 2 and max((len(row) for row in matrix), default=0) >= 2


def to_table_contract(
    source_path: Path,
    page: int,
    matrix: list[list[str]],
    confidence: float,
    warnings: list[str],
) -> dict[str, Any]:
    columns = make_columns(matrix[0], len(matrix[0]))
    return {
        "sourceFilePath": str(source_path),
        "pageNumber": max(1, page),
        "columns": columns,
        "rows": matrix,
        "confidence": normalize_confidence(confidence),
        "warnings": unique_messages(warnings),
    }


def make_columns(header_row: list[str], width: int) -> list[str]:
    result: list[str] = []
    used: set[str] = set()
    for index in range(width):
        name = clean_value(header_row[index] if index < len(header_row) else "")
        if not name:
            name = f"Колонка {index + 1}"

        base_name = name
        suffix = 2
        while name in used:
            name = f"{base_name} {suffix}"
            suffix += 1

        used.add(name)
        result.append(name)

    return result


def page_number(table: Any) -> int:
    value = getattr(table, "page", 1)
    try:
        return max(1, int(value))
    except (TypeError, ValueError):
        return 1


def clean_value(value: Any) -> str:
    if value is None:
        return ""

    return re.sub(r"\s+", " ", str(value).replace("\u00a0", " ")).strip()


def normalize_confidence(value: float) -> float:
    if value <= 0:
        return 0.0

    if value > 1:
        return 1.0

    return round(value, 4)


def unique_messages(messages: list[str]) -> list[str]:
    result: list[str] = []
    for message in messages:
        value = clean_value(message)
        if value and value not in result:
            result.append(value)

    return result


def empty_result(
    warnings: list[str] | None = None,
    errors: list[str] | None = None,
) -> dict[str, Any]:
    return {
        "tables": [],
        "warnings": unique_messages(warnings or []),
        "errors": unique_messages(errors or []),
    }


def write_json(output_path: Path, result: dict[str, Any]) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(
        json.dumps(result, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
