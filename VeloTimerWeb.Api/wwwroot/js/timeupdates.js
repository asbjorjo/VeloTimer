"use strict";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hub/passing")
    .configureLogging(signalR.LogLevel.Information)
    .build();

var _segmentid;
var _time = new Date();

async function start() {
    try {
        connection.on("NewSegmentRun", function (segmentrun) {
            refreshTimes();
        });
        await connection.start();
        console.log("SignalR Connected.");
        if (_segmentid) {
            await connection.invoke("AddToSegmentGroup", _segmentid);
        }
    } catch (err) {
        console.log(err);
        setTimeout(start, 5000);
    }
};

async function subscribetosegment(segmentid) {
    _segmentid = segmentid;
    await start();
    _time = new Date();
};

function refreshTimes() {
    //$.ajax({
    //    type: 'GET',
    //    url: '?handler=NewTime',
    //    success: function (data) {
    //        console.log(data);
    //    },
    //    error: function (error) {
    //        console.log(error);
    //    }
    //})

    var currentTime = new Date();

    if (currentTime.getTime() - _time.getTime() > 15000) {
        console.log("Should reload");
        window.location.reload();
        _time = currentTime;
    }        
}

connection.onclose(async () => {
    await start();
});