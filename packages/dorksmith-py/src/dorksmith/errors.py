"""Error type raised for invalid requests."""
from __future__ import annotations


class InputValidationError(ValueError):
    """Invalid request. ``unprocessable`` distinguishes 422-style (valid shape, cannot generate) from 400-style errors."""

    def __init__(self, message: str, field: str | None = None, unprocessable: bool = False) -> None:
        super().__init__(message)
        self.message = message
        self.field = field
        self.unprocessable = unprocessable

    def __str__(self) -> str:  # pragma: no cover - trivial
        return self.message
