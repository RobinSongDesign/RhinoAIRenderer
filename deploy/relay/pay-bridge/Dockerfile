# Payment bridge: new-api's Epay protocol to the operator's own Alipay merchant account.
FROM python:3.12-alpine
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY pay_bridge.py .
# Orders (SQLite) and the Alipay application key live in the /data volume.
VOLUME /data
EXPOSE 8090
CMD ["python", "-u", "/app/pay_bridge.py", "serve"]
