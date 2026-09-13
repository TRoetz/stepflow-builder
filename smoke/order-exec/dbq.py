"""dbq.py - minimal SQLite query helper for the StepFlow order-exec test flow.

Usage: python dbq.py <sql-file>
  SELECT ... -> prints {"rows": [ {col: val, ...}, ... ]}
  DML        -> commits and prints {"changes": N}
"""
import json
import sqlite3
import sys

DB = r"C:\temp\order-exec\orders.db"


def main() -> None:
    sql_path = sys.argv[1]
    with open(sql_path, encoding="utf-8-sig") as f:
        sql = f.read().strip()
    conn = sqlite3.connect(DB, timeout=15)
    conn.row_factory = sqlite3.Row
    try:
        before = conn.total_changes
        cur = conn.execute(sql)
        if cur.description is not None:
            rows = [dict(r) for r in cur.fetchall()]
            print(json.dumps({"rows": rows}, default=str))
        else:
            changes = cur.rowcount
            if changes < 0:
                changes = conn.total_changes - before
            conn.commit()
            print(json.dumps({"changes": changes}))
    finally:
        conn.close()


if __name__ == "__main__":
    main()
