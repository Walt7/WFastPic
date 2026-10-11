#!/bin/bash
set -e
set -o pipefail
APP_DIR="/home/walter/WFastPic"
LOG_FILE="$APP_DIR/deploy/deploy.log"
LOCK_FILE="$APP_DIR/deploy/deploy.lock"
NTFY_TOPIC="vps-walterdev-problem"
log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "$LOG_FILE"; }
notify() { curl -s -o /dev/null -d "$2" -H "Title: $1" -H "Tags: $3" "https://ntfy.sh/$NTFY_TOPIC" || true; }

main() {
  trap 'notify "❌ WFastPic deploy fallito" "Errore nel deploy di WFastPic sul VPS" "warning"' ERR

  exec 9>"$LOCK_FILE"
  if ! flock -w 300 9; then
    log "Timeout attesa lock, deploy annullato."
    exit 1
  fi

  log "=== Deploy WFastPic avviato ==="
  cd "$APP_DIR"

  PREV=$(git rev-parse HEAD)
  git fetch origin sito
  git reset --hard origin/sito
  NEW=$(git rev-parse HEAD)

  if ! git diff --quiet "$PREV" "$NEW" -- contatore/; then
    log "contatore/ cambiato: test, build e restart del servizio"
    export PATH="$PATH:/usr/local/go/bin"
    (cd "$APP_DIR/contatore" && go test ./... && go build -o contatore .)
    sudo -n /usr/bin/systemctl restart wfastpic-contatore
  fi

  if ! git diff --quiet "$PREV" "$NEW" -- nginx-wfastpic.conf; then
    log "nginx-wfastpic.conf cambiato: va aggiornato a mano"
    notify "⚠️ WFastPic: aggiorna nginx" "nginx-wfastpic.conf è cambiato: copia le modifiche nella config attiva di nginx e fai reload" "warning"
  fi

  log "=== Deploy WFastPic completato ==="
  notify "✅ WFastPic deployato" "Deploy completato con successo" "white_check_mark"
}

main
