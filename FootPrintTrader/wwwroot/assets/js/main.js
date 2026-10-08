function callBackground(message){
    window.external.sendMessage(message);
}

const handlers = {
    FOOTPRINT_CURRENT: footprintCurrent
};

function receiveFromBackground(message){
    const { Type, Data } = JSON.parse(message);

    if (handlers[Type]) {
        handlers[Type](Data);
    }
}

function footprintCurrent(data){
    const el = document.getElementById('footprint-dump');
    if (!el) return;
    el.textContent = JSON.stringify(data, null, 2);
}
