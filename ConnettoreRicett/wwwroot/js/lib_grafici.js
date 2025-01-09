function getGraficoLinea(id, titolo, labels, data) {
    var options = {
        series: data,
        chart: {
            height: 350,
            type: 'line',
            zoom: {
                enabled: false
            }
        },
        dataLabels: {
            enabled: false
        },
        stroke: {
            curve: 'straight'
        },
        title: {
            text: titolo,
            align: 'left'
        },
        grid: {
            row: {
                colors: ['#f3f3f3', 'transparent'], // takes an array which will be repeated on columns
                opacity: 0.5
            },
        },
        xaxis: {
            categories: labels,
        }
    };

    var chart = new ApexCharts(document.querySelector("#" + id), options);
    chart.render();
}