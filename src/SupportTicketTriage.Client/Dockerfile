# package.json and the lockfile are copied on their own so that `npm ci` is
# cached against the dependency manifest rather than invalidated by every
# source edit - the same layer-caching reason the API's Dockerfile copies its
# .csproj files before the rest of the source.
FROM node:24-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:1.29-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
