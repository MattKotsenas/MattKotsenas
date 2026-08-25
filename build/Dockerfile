FROM hugomods/hugo:0.145.0@sha256:879cf9ff9c411cf9a2904d466bedcac1f26a8c09fcdb663ca90dcf94a47f49cb AS hugo
WORKDIR /src

FROM hugo AS dev

FROM hugo AS build
COPY . .
RUN hugo --minify

FROM scratch AS export
COPY --from=build /src/public /

FROM nginxinc/nginx-unprivileged:1.30.4-alpine-slim@sha256:bcf91d2c73ab64fa1c4ac7fbac5ac523057c8af7d553ab9251c7aef38c260979 AS final
COPY build/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/public /usr/share/nginx/html
