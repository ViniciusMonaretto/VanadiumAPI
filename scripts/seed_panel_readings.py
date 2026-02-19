#!/usr/bin/env python3
"""
Insere dados na collection PanelReadings do banco MyDb.
- Período: de 30 dias atrás até o minuto atual (inclui o dia de hoje).
- Um documento por minuto. PanelId: 1, Value: 1.0.
- Formato: { PanelId, ReadingTime (ISODate), Value }; _id gerado pelo MongoDB.

Requer: pip install pymongo
"""

from datetime import datetime, timezone, timedelta
from pymongo import MongoClient

# Configuração MongoDB (igual ao docker-compose)
MONGO_URI = "mongodb://root:example@localhost:27017/?authSource=admin"
DB_NAME = "MyDb"
COLLECTION_NAME = "PanelReadings"
PANEL_ID = 1
VALUE = 1.0
DAYS = 30
BATCH_SIZE = 5000


def main():
    client = MongoClient(MONGO_URI)
    db = client[DB_NAME]
    coll = db[COLLECTION_NAME]

    # De 30 dias atrás até o minuto atual (inclui hoje): um documento por minuto
    now = datetime.now(timezone.utc)
    start_time = (now - timedelta(days=DAYS)).replace(second=0, microsecond=0)
    end_time = now.replace(second=0, microsecond=0)

    total_minutes = int((end_time - start_time).total_seconds() / 60) + 1
    print(f"Inserindo {total_minutes} documentos (de 30 dias atrás até hoje, 1 por minuto)...")
    print(f"Período: {start_time.isoformat()} até {end_time.isoformat()} (inclusive)")

    inserted = 0
    batch = []
    current = start_time

    while current <= end_time:
        batch.append({
            "PanelId": PANEL_ID,
            "ReadingTime": current,
            "Value": VALUE,
        })
        current += timedelta(minutes=1)

        if len(batch) >= BATCH_SIZE:
            coll.insert_many(batch)
            inserted += len(batch)
            print(f"  Inseridos {inserted}/{total_minutes}")
            batch = []

    if batch:
        coll.insert_many(batch)
        inserted += len(batch)

    print(f"Concluído. Total inserido: {inserted} documentos.")
    client.close()


if __name__ == "__main__":
    main()
