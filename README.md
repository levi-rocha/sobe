# SOB
Service d'Obtention de Binaires

## Execution

- Standard
```sh
# Uses HTTPS
dotnet run
```

- Docker
```sh
# Uses HTTP
docker build -t sob .
docker run --rm -dit -p 5000:80 --name=sob sob
```

## Usage

- Display Usage
```sh
curl -i -s -w "\n" http://localhost:5000/
```

- Request Download
```sh
curl -i -s -w "\n" http://localhost:5000/download -XPOST -H "Content-Type: application/json" -d '{"FileUrl": "http://example.com/example.exe", "OutputName": "myexe.exe"}'  
# {"requestId": REQUESTID, "ready": false}
```

- Check Requested Download Status
```sh
curl -i -s -w "\n" http://localhost:5000/download/status?requestId=REQUESTID  
# {"requestId": REQUESTID, "ready": true}
```
  
- Download Zipped Processed File
```sh
curl -i -s -w "\n" http://localhost:5000/download?requestId=REQUESTID --output myzip.zip
unzip myzip.zip zip
chmod 0755 zip/myexe.exe
```  
