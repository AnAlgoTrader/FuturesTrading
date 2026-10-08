function callBackground(message){
    window.external.sendMessage(message);
}

const handlers = {
    FOOTPRINT_SNAPSHOT: footprintSnapshot,
    CAPTURE_CHART: captureChart
};

let lastSignature = null;

function receiveFromBackground(message){
    const { Type, Data } = JSON.parse(message);

    if (handlers[Type]) {
        handlers[Type](Data);
    }
}

function footprintSnapshot(data){
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

function captureChart(){
    let data = null;
    try {
        const canvas = document.getElementById('cv');
        if (canvas) data = canvas.toDataURL('image/png');
    } catch (e) {
        data = null;
    }

    window.external.sendMessage(JSON.stringify({Type: 'CHART_CAPTURE', Data: data}));
}
