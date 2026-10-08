function callBackground(message){
    window.external.sendMessage(message);
}

const handlers = {
    FOOTPRINT_SNAPSHOT: footprintSnapshot
};

let lastSignature = null;

function receiveFromBackground(message){
    const { Type, Data } = JSON.parse(message);

    if (handlers[Type]) {
        handlers[Type](Data);
    }
}

function footprintSnapshot(data){
    const el = document.getElementById('footprint-dump');
    if (el) el.textContent = JSON.stringify(data, null, 2);

    if (!window.footprint || !Array.isArray(data.bars) || !data.bars.length) return;

    const bars = data.bars;
    const signature = bars.length + ':' + bars[0].id;

    if (signature !== lastSignature) {
        lastSignature = signature;
        window.footprint.load(data);
    } else {
        window.footprint.updateLast(bars[bars.length - 1]);
    }
}
