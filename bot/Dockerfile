FROM python:3.12-slim
WORKDIR /app
COPY . .
RUN pip install --no-cache-dir . && rm -rf /app
USER nobody
CMD ["ratatoskr-bot"]
