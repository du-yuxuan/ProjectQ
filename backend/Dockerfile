FROM node:22-slim
LABEL "language"="nodejs"
WORKDIR /src
RUN apt-get update -y && apt-get install -y openssl && rm -rf /var/lib/apt/lists/*
COPY . .
RUN npm install
RUN npx prisma generate --schema src/db/schema.prisma
RUN npm run build
EXPOSE 8080
CMD ["sh", "-c", "npx prisma db push --schema src/db/schema.prisma --accept-data-loss && node dist/app.js"]
