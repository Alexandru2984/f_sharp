#!/bin/bash
cat << 'CSV' > /home/micu/f_sharp/data/demo.csv
Date,Amount,Currency,Category,Merchant,Description
2026-04-01T10:00:00,50.0,USD,Groceries,Walmart,Weekly food
2026-04-05T12:00:00,60.0,USD,Groceries,Walmart,Weekly food
2026-04-10T14:00:00,55.0,USD,Groceries,Walmart,Weekly food
2026-04-15T09:00:00,20.0,USD,Transport,Uber,Ride to work
2026-04-16T18:00:00,25.0,USD,Transport,Uber,Ride home
2026-04-20T10:00:00,300.0,USD,Groceries,Walmart,Unusually large grocery trip
2026-04-22T20:00:00,10.0,USD,Entertainment,Netflix,Subscription
2026-05-01T10:00:00,50.0,USD,Groceries,Walmart,Weekly food
2026-05-02T19:00:00,500.0,USD,Entertainment,BestBuy,New TV
2026-05-03T02:00:00,150.0,USD,Dining,Bar,Late night drinks
2026-05-03T12:00:00,100.0,USD,Entertainment,Walmart,Unusual category for Walmart
CSV

curl -c cookies.txt -X POST -H "Content-Type: application/json" -d '{"username": "admin", "password": "admin123"}' http://localhost:5000/api/login
curl -b cookies.txt -F "file=@/home/micu/f_sharp/data/demo.csv" http://localhost:5000/api/expenses/import-csv
curl -b cookies.txt -X POST http://localhost:5000/api/anomalies/run
rm cookies.txt
echo "Demo data seeded."
