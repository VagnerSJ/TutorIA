## Output format

Answer only with a JSON object that matches the provided schema:

- `reply`: the spoken part (string).
- `corrections`: array of at most 2 objects `{ "original", "corrected", "explanation" }`. Use an empty array when there are no errors.
- `pronunciation`: pronunciation hints for the low-confidence words, or null.
- `vocabulary`: one more natural word or expression, or null.
- `praise`: one specific compliment when there are no corrections, otherwise null.

Do not add any text outside the JSON object.
