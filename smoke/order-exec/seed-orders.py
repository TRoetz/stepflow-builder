"""Seed C:\\temp\\order-exec\\orders.db with a small coffee-equipment catalog.

Tables:
  products(id, name, price, stock, category, description)
  complements(product_id, complement_id)  -- "goes well with" pairs
Idempotent: drops and recreates both tables on every run.
"""
import os
import sqlite3

DB_DIR = r"C:\temp\order-exec"
DB = os.path.join(DB_DIR, "orders.db")

PRODUCTS = [
    (1, "Aurora Espresso Machine", 549.00, 12, "Machines",
     "Semi-automatic espresso machine with PID temperature control and a 15-bar pump."),
    (2, "Precision Burr Grinder", 189.00, 20, "Grinders",
     "Conical burr grinder with 40 grind settings, from fine espresso to coarse French press."),
    (3, "Single-Origin Espresso Beans (1kg)", 24.00, 60, "Beans",
     "Medium-dark roast Colombian beans with notes of chocolate and caramel."),
    (4, "Milk Frother Carafe", 39.00, 25, "Accessories",
     "Stainless steel carafe with micro-foam wand for latte art."),
    (5, "Portafilter Tamper Set", 59.00, 18, "Accessories",
     "60mm tamper and distributor set with walnut handle."),
    (6, "Cold Brew Tower", 79.00, 14, "Brewers",
     "Countertop cold brew tower with 4L capacity and drip-free valve."),
    (7, "Pour-Over Kettle (Gooseneck)", 65.00, 16, "Brewers",
     "Matte black gooseneck kettle with built-in thermometer for slow pours."),
    (8, "Ceramic Espresso Cups (set of 4)", 32.00, 30, "Accessories",
     "Hand-glazed ceramic cups, 90ml each."),
]

COMPLEMENTS = [
    (1, 2), (1, 3), (1, 5),   # espresso machine -> grinder, beans, tamper set
    (2, 1), (2, 3),           # grinder -> machine, beans
    (3, 1), (3, 2),           # beans -> machine, grinder
    (4, 1),                   # frother carafe -> espresso machine
    (5, 1),                   # tamper set -> espresso machine
    (6, 3),                   # cold brew tower -> beans
    (7, 8), (7, 3),           # gooseneck kettle -> cups, beans
    (8, 1), (8, 4),           # cups -> machine, frother carafe
]


def main() -> None:
    os.makedirs(DB_DIR, exist_ok=True)
    conn = sqlite3.connect(DB)
    cur = conn.cursor()
    cur.execute("DROP TABLE IF EXISTS complements")
    cur.execute("DROP TABLE IF EXISTS products")
    cur.execute(
        "CREATE TABLE products ("
        " id INTEGER PRIMARY KEY,"
        " name TEXT NOT NULL,"
        " price REAL NOT NULL,"
        " stock INTEGER NOT NULL,"
        " category TEXT NOT NULL,"
        " description TEXT NOT NULL)"
    )
    cur.executemany("INSERT INTO products VALUES (?,?,?,?,?,?)", PRODUCTS)
    cur.execute(
        "CREATE TABLE complements ("
        " product_id INTEGER NOT NULL REFERENCES products(id),"
        " complement_id INTEGER NOT NULL REFERENCES products(id))"
    )
    cur.executemany("INSERT INTO complements VALUES (?,?)", COMPLEMENTS)
    conn.commit()
    n_products = cur.execute("SELECT COUNT(*) FROM products").fetchone()[0]
    n_compl = cur.execute("SELECT COUNT(*) FROM complements").fetchone()[0]
    print(f"seeded {DB}: {n_products} products, {n_compl} complement pairs")
    conn.close()


if __name__ == "__main__":
    main()
