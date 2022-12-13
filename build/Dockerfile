FROM node:8.9.0 as builder

WORKDIR /app
COPY . .
RUN npm install --production
RUN cd src && npm test
ENTRYPOINT [ "sh" ]