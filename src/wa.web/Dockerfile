# syntax=docker/dockerfile:1

# Build stage — compiles the React SPA (needs node_modules for Vite + TypeScript)
FROM node:20-slim AS build
WORKDIR /app

COPY src/wa.web/package.json src/wa.web/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY src/wa.web/ .
# tsc -b && vite build (package.json "build" script)
RUN npm run build

# Serve stage — nginx with SPA fallback on port 5173
FROM nginx:alpine AS final
COPY src/wa.web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 5173
CMD ["nginx", "-g", "daemon off;"]
